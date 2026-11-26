using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DirectLinkShare
{
    public sealed class ShareStatistics
    {
        public long BytesSent;
        public long CompletedRequests;
        public int ActiveConnections;
    }

    // Deliberately serves ONE immutable file, not a folder or arbitrary request paths.
    public sealed class LocalFileServer : IDisposable
    {
        private readonly string filePath;
        private readonly string decodedPath;
        private readonly string disposition;
        private readonly string etag;
        private readonly DateTime lastModified;
        private readonly FileStream anchor;
        private readonly TcpListener listener;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly HashSet<TcpClient> clients = new HashSet<TcpClient>();
        private readonly object clientsLock = new object();
        private readonly Task accepting;
        private long bytesSent;
        private long completedRequests;
        private int activeConnections;
        private int disposed;
        public readonly string Origin;
        public readonly string RelativePath;
        public readonly string FileName;
        public readonly long Length;

        public LocalFileServer(string path)
        {
            filePath = Path.GetFullPath(path);
            FileName = Path.GetFileName(filePath);
            anchor = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                Length = anchor.Length;
                lastModified = File.GetLastWriteTimeUtc(filePath);
                lastModified = new DateTime(lastModified.Ticks - lastModified.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
                byte[] secret = new byte[32];
                using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(secret);
                string token = BitConverter.ToString(secret).Replace("-", "").ToLowerInvariant();
                decodedPath = "/d/" + token + "/" + FileName;
                RelativePath = "/d/" + token + "/" + Uri.EscapeDataString(FileName);
                etag = "\"" + token.Substring(0, 24) + "-" + Length.ToString("x") + "\"";
                StringBuilder ascii = new StringBuilder();
                foreach (char c in FileName) ascii.Append(c >= 32 && c <= 126 && c != '"' && c != '\\' ? c : '_');
                disposition = "attachment; filename=\"" + ascii + "\"; filename*=UTF-8''" + Uri.EscapeDataString(FileName);
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start(128);
                Origin = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
                accepting = Task.Run(() => AcceptAsync());
            }
            catch { anchor.Dispose(); throw; }
        }

        public ShareStatistics GetStatistics()
        {
            return new ShareStatistics { BytesSent = Interlocked.Read(ref bytesSent),
                CompletedRequests = Interlocked.Read(ref completedRequests), ActiveConnections = Volatile.Read(ref activeConnections) };
        }

        private async Task AcceptAsync()
        {
            while (!stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { if (stop.IsCancellationRequested) break; throw; }
                client.NoDelay = true;
                client.SendTimeout = 30000;
                client.ReceiveTimeout = 15000;
                bool accepted;
                lock (clientsLock)
                {
                    accepted = !stop.IsCancellationRequested && clients.Count < 64;
                    if (accepted) clients.Add(client);
                }
                if (!accepted) { client.Close(); continue; }
                // Every operation catches its own transport errors and unregisters the client.
                Task work = ServeAsync(client);
                Observe(work);
            }
        }

        private static async void Observe(Task task)
        {
            try { await task.ConfigureAwait(false); }
            catch (Exception) { /* A failed connection must never terminate the file server. */ }
        }

        private async Task ServeAsync(TcpClient client)
        {
            bool transferring = false;
            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                using (CancellationTokenSource requestStop = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
                {
                    requestStop.CancelAfter(15000);
                    using (requestStop.Token.Register(client.Close))
                    {
                        string header = await ReadHeadersAsync(stream, requestStop.Token).ConfigureAwait(false);
                        if (header == null) return;
                        string[] lines = header.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                        string[] first = lines[0].Split(' ');
                        if (first.Length != 3 || !first[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
                        { await ReplyAsync(stream, 400, "Bad Request", 0, null, false).ConfigureAwait(false); return; }
                        string method = first[0];
                        bool head = method == "HEAD";
                        if (method != "GET" && !head)
                        { await ReplyAsync(stream, 405, "Method Not Allowed", 0, "Allow: GET, HEAD\r\n", false).ConfigureAwait(false); return; }
                        string requested;
                        try { requested = Uri.UnescapeDataString(first[1].Split('?')[0]); }
                        catch (UriFormatException) { requested = ""; }
                        if (requested != decodedPath)
                        { await ReplyAsync(stream, 404, "Not Found", 0, null, false).ConfigureAwait(false); return; }
                        Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 1; i < lines.Length; i++)
                        {
                            int colon = lines[i].IndexOf(':');
                            if (colon <= 0) continue;
                            string name = lines[i].Substring(0, colon).Trim();
                            if (headers.ContainsKey(name))
                            { await ReplyAsync(stream, 400, "Bad Request", 0, null, false).ConfigureAwait(false); return; }
                            headers[name] = lines[i].Substring(colon + 1).Trim();
                        }
                        string conditional;
                        if (headers.TryGetValue("If-None-Match", out conditional) && (conditional == etag || conditional == "*"))
                        { await ReplyAsync(stream, 304, "Not Modified", 0, null, true).ConfigureAwait(false); return; }

                        long from = 0, to = Length - 1;
                        string range;
                        // RFC 9110: a Range header modifies GET, not HEAD.
                        bool ranged = !head && headers.TryGetValue("Range", out range);
                        if (ranged && headers.TryGetValue("If-Range", out conditional))
                        {
                            DateTime date;
                            if (conditional != etag && (!DateTime.TryParse(conditional, CultureInfo.InvariantCulture,
                                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out date) || date < lastModified)) ranged = false;
                        }
                        if (ranged)
                        {
                            range = headers["Range"];
                            if (!range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) ranged = false;
                            else if (!TryRange(range, Length, out from, out to))
                            { await ReplyAsync(stream, 416, "Range Not Satisfiable", 0, "Content-Range: bytes */" + Length + "\r\n", true).ConfigureAwait(false); return; }
                        }
                        long count = ranged ? to - from + 1 : Length;
                        string extra = ranged ? "Content-Range: bytes " + from + "-" + to + "/" + Length + "\r\n" : null;
                        // A slow receiver gets a fresh inactivity deadline for each read/write.
                        requestStop.CancelAfter(30000);
                        await ReplyAsync(stream, ranged ? 206 : 200, ranged ? "Partial Content" : "OK", count, extra, true).ConfigureAwait(false);
                        if (head) return;
                        Interlocked.Increment(ref activeConnections); transferring = true;
                        using (FileStream file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 262144,
                            FileOptions.Asynchronous | FileOptions.SequentialScan))
                        {
                            file.Seek(ranged ? from : 0, SeekOrigin.Begin);
                            byte[] buffer = new byte[262144];
                            long remaining = count;
                            while (remaining > 0)
                            {
                                requestStop.CancelAfter(30000);
                                int read = await file.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, remaining), requestStop.Token).ConfigureAwait(false);
                                if (read == 0) throw new IOException("File changed while being served.");
                                await stream.WriteAsync(buffer, 0, read, requestStop.Token).ConfigureAwait(false);
                                Interlocked.Add(ref bytesSent, read);
                                remaining -= read;
                            }
                        }
                        Interlocked.Increment(ref completedRequests);
                    }
                }
            }
            catch (IOException) { }
            catch (SocketException) { }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                if (transferring) Interlocked.Decrement(ref activeConnections);
                lock (clientsLock) clients.Remove(client);
                client.Close();
            }
        }

        private async Task ReplyAsync(Stream stream, int code, string reason, long length, string extra, bool metadata)
        {
            StringBuilder text = new StringBuilder("HTTP/1.1 " + code + " " + reason + "\r\n");
            text.Append("Connection: close\r\nContent-Length: ").Append(length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            text.Append("Cache-Control: no-store, no-transform\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\n");
            if (metadata)
            {
                text.Append("Accept-Ranges: bytes\r\nContent-Type: application/octet-stream\r\n");
                text.Append("Content-Disposition: ").Append(disposition).Append("\r\n");
                text.Append("ETag: ").Append(etag).Append("\r\nLast-Modified: ").Append(lastModified.ToString("R", CultureInfo.InvariantCulture)).Append("\r\n");
            }
            if (extra != null) text.Append(extra);
            text.Append("\r\n");
            byte[] bytes = Encoding.ASCII.GetBytes(text.ToString());
            await stream.WriteAsync(bytes, 0, bytes.Length, stop.Token).ConfigureAwait(false);
        }

        private static async Task<string> ReadHeadersAsync(Stream stream, CancellationToken token)
        {
            byte[] buffer = new byte[16384];
            int used = 0;
            while (used < buffer.Length)
            {
                int n = await stream.ReadAsync(buffer, used, buffer.Length - used, token).ConfigureAwait(false);
                if (n == 0) return null;
                int previous = used;
                used += n;
                for (int i = Math.Max(3, previous); i < used; i++)
                    if (buffer[i - 3] == 13 && buffer[i - 2] == 10 && buffer[i - 1] == 13 && buffer[i] == 10)
                        return Encoding.ASCII.GetString(buffer, 0, i - 3);
            }
            return null;
        }

        public static bool TryRange(string value, long length, out long from, out long to)
        {
            from = 0; to = length - 1;
            if (length == 0 || value == null || !value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
            string[] bounds = value.Substring(6).Trim().Split('-');
            if (bounds.Length != 2) return false;
            long number;
            if (bounds[0].Length == 0)
            {
                if (!long.TryParse(bounds[1], NumberStyles.None, CultureInfo.InvariantCulture, out number) || number <= 0) return false;
                from = Math.Max(0, length - number); return true;
            }
            if (!long.TryParse(bounds[0], NumberStyles.None, CultureInfo.InvariantCulture, out from) || from >= length) return false;
            if (bounds[1].Length > 0)
            {
                if (!long.TryParse(bounds[1], NumberStyles.None, CultureInfo.InvariantCulture, out number) || number < from) return false;
                to = Math.Min(number, length - 1);
            }
            return true;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            stop.Cancel(); listener.Stop();
            TcpClient[] snapshot;
            lock (clientsLock) { snapshot = new TcpClient[clients.Count]; clients.CopyTo(snapshot); }
            foreach (TcpClient client in snapshot) client.Close();
            try { accepting.Wait(2000); } catch (AggregateException) { }
            anchor.Dispose();
        }
    }
}
