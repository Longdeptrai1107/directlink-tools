using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DirectLinkDownloader
{
    public sealed class DownloadProgress
    {
        public long Received;
        public long Total;
        public int Connections;
        public string Status;
        public string FileName;
    }

    public sealed class DownloadResult
    {
        public long Bytes;
        public int Connections;
        public string Path;
    }

    internal sealed class RemoteInfo
    {
        public Uri Uri;
        public long Length;
        public bool Ranges;
        public string Validator;
        public string Disposition;
    }

    public sealed class DownloadEngine
    {
        private long received;
        private long total = -1;
        private volatile int connections;
        private volatile string status = "Sẵn sàng";
        private volatile string currentPath;
        private int running;
        private const int BufferSize = 262144;

        public DownloadProgress GetProgress()
        {
            return new DownloadProgress { Received = Interlocked.Read(ref received),
                Total = Interlocked.Read(ref total), Connections = connections, Status = status,
                FileName = currentPath == null ? null : System.IO.Path.GetFileName(currentPath) };
        }

        public Task<DownloadResult> DownloadAsync(string address, string destination, int threads, CancellationToken token)
        {
            return Task.Run(() => Download(address, destination, threads, token, false, null));
        }

        public Task<DownloadResult> DownloadToFolderAsync(string address, string folder, string preferredName, int threads, CancellationToken token)
        {
            return Task.Run(() => Download(address, folder, threads, token, true, preferredName));
        }

        private DownloadResult Download(string address, string destination, int threads, CancellationToken token, bool automaticName, string preferredName)
        {
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
                throw new InvalidOperationException("Một lượt tải đang chạy.");
            string temp = null;
            try
            {
                Uri uri;
                if (!Uri.TryCreate(address, UriKind.Absolute, out uri) ||
                    (uri.Scheme != "http" && uri.Scheme != "https"))
                    throw new ArgumentException("Hãy nhập link HTTP hoặc HTTPS hợp lệ.");
                threads = Math.Max(1, Math.Min(32, threads));
                destination = System.IO.Path.GetFullPath(destination);
                string folder = automaticName ? destination : System.IO.Path.GetDirectoryName(destination);
                if (!Directory.Exists(folder))
                    throw new DirectoryNotFoundException("Thư mục lưu không tồn tại.");
                currentPath = automaticName ? null : destination;
                Interlocked.Exchange(ref received, 0);
                Interlocked.Exchange(ref total, -1);
                connections = 0;
                status = "Đang kiểm tra máy chủ...";
                token.ThrowIfCancellationRequested();
                RemoteInfo info = Probe(uri, token);
                string automaticFileName = null;
                if (automaticName)
                {
                    automaticFileName = DownloadPaths.ChooseFileName(preferredName, info.Disposition, info.Uri);
                    destination = DownloadPaths.AvailablePath(folder, automaticFileName);
                }
                currentPath = destination;
                Interlocked.Exchange(ref total, info.Length);
                // Very small files do not benefit from many connections.
                int count = info.Ranges && info.Length > 0
                    ? (int)Math.Min(threads, Math.Max(1L, (info.Length + 262143) / 262144)) : 1;
                connections = count;
                temp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(destination),
                    ".directlink-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temp);
                status = count > 1 ? "Đang tải song song..." : "Đang tải một luồng...";
                List<string> parts = new List<string>();
                using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    List<Task> tasks = new List<Task>();
                    Exception firstFailure = null;
                    object failureLock = new object();
                    for (int i = 0; i < count; i++)
                    {
                        int index = i;
                        string part = System.IO.Path.Combine(temp, index.ToString("D3") + ".part");
                        parts.Add(part);
                        long start = info.Length > 0 ? info.Length / count * index : 0;
                        long end = info.Length > 0
                            ? (index == count - 1 ? info.Length - 1 : info.Length / count * (index + 1) - 1) : -1;
                        tasks.Add(Task.Factory.StartNew(() =>
                        {
                            try { DownloadPart(info, part, start, end, info.Ranges && info.Length > 0, linked.Token); }
                            catch (Exception error)
                            {
                                lock (failureLock) { if (firstFailure == null) firstFailure = error; }
                                linked.Cancel();
                                throw;
                            }
                        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
                    }
                    try { Task.WaitAll(tasks.ToArray()); }
                    catch (AggregateException)
                    {
                        token.ThrowIfCancellationRequested();
                        throw firstFailure ?? new IOException("Không tải được đầy đủ các phần.");
                    }
                }
                token.ThrowIfCancellationRequested();
                status = "Đang ghép và kiểm tra kích thước...";
                string payload = System.IO.Path.Combine(temp, "complete.tmp");
                byte[] buffer = new byte[BufferSize];
                using (FileStream output = new FileStream(payload, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize))
                {
                    foreach (string part in parts)
                    {
                        using (FileStream input = File.OpenRead(part))
                        {
                            int n;
                            while ((n = input.Read(buffer, 0, buffer.Length)) != 0)
                            {
                                token.ThrowIfCancellationRequested();
                                output.Write(buffer, 0, n);
                            }
                        }
                    }
                    if (info.Length >= 0 && output.Length != info.Length)
                        throw new IOException("Kích thước file không khớp. File chưa được lưu.");
                    output.Flush(true);
                }
                token.ThrowIfCancellationRequested();
                // Keep the old destination intact until a complete replacement is available.
                if (automaticName)
                {
                    // File.Move never overwrites: another app may create this name while downloading.
                    while (true)
                    {
                        destination = DownloadPaths.AvailablePath(folder, automaticFileName);
                        try { File.Move(payload, destination); break; }
                        catch (IOException) { if (!File.Exists(destination) && !Directory.Exists(destination)) throw; }
                    }
                }
                else if (File.Exists(destination)) File.Replace(payload, destination, null);
                else File.Move(payload, destination);
                currentPath = destination;
                status = "Hoàn tất";
                return new DownloadResult { Bytes = new FileInfo(destination).Length, Connections = count, Path = destination };
            }
            catch (OperationCanceledException) { status = "Đã hủy"; throw; }
            catch { status = "Tải thất bại"; throw; }
            finally
            {
                if (temp != null)
                {
                    try { Directory.Delete(temp, true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                Interlocked.Exchange(ref running, 0);
            }
        }

        private static HttpWebRequest Request(Uri uri)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.UserAgent = "DirectLinkDownloader/1.0";
            request.AllowAutoRedirect = true;
            request.MaximumAutomaticRedirections = 10;
            request.AutomaticDecompression = DecompressionMethods.None;
            request.Headers[HttpRequestHeader.AcceptEncoding] = "identity";
            request.Timeout = 20000;
            request.ReadWriteTimeout = 20000;
            request.KeepAlive = true;
            request.ServicePoint.ConnectionLimit = Math.Max(request.ServicePoint.ConnectionLimit, 40);
            return request;
        }

        private static void CheckEncoding(HttpWebResponse response)
        {
            string encoding = response.Headers[HttpResponseHeader.ContentEncoding];
            if (!string.IsNullOrEmpty(encoding) && !encoding.Equals("identity", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Máy chủ trả nội dung nén không phù hợp để ghép từng phần.");
        }

        private static bool ParseRange(string value, out long start, out long end, out long length)
        {
            start = end = length = -1;
            Match match = Regex.Match(value ?? "", @"^bytes (\d+)-(\d+)/(\d+)$", RegexOptions.IgnoreCase);
            return match.Success && long.TryParse(match.Groups[1].Value, out start)
                && long.TryParse(match.Groups[2].Value, out end) && long.TryParse(match.Groups[3].Value, out length);
        }

        private static RemoteInfo Probe(Uri uri, CancellationToken token)
        {
            HttpWebRequest request = Request(uri);
            request.AddRange(0, 0);
            using (token.Register(request.Abort))
            {
                try
                {
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        CheckEncoding(response);
                        RemoteInfo result = new RemoteInfo { Uri = response.ResponseUri, Length = response.ContentLength,
                            Disposition = response.Headers["Content-Disposition"] };
                        string etag = response.Headers[HttpResponseHeader.ETag];
                        result.Validator = !string.IsNullOrEmpty(etag) && !etag.StartsWith("W/", StringComparison.OrdinalIgnoreCase)
                            ? etag : response.Headers[HttpResponseHeader.LastModified];
                        if (response.StatusCode == HttpStatusCode.PartialContent)
                        {
                            long start, end, length;
                            if (!ParseRange(response.Headers[HttpResponseHeader.ContentRange], out start, out end, out length)
                                || start != 0 || end != 0 || length < 1)
                                throw new InvalidDataException("Máy chủ trả Content-Range không hợp lệ.");
                            result.Length = length;
                            result.Ranges = true;
                        }
                        else if (response.StatusCode != HttpStatusCode.OK)
                            throw new IOException("Máy chủ trả mã HTTP không được hỗ trợ: " + (int)response.StatusCode);
                        return result;
                    }
                }
                catch (WebException error)
                {
                    token.ThrowIfCancellationRequested();
                    HttpWebResponse response = error.Response as HttpWebResponse;
                    if (response != null)
                    {
                        using (response)
                        {
                            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable
                                && response.Headers[HttpResponseHeader.ContentRange] == "bytes */0")
                                return new RemoteInfo { Uri = response.ResponseUri, Length = 0, Ranges = false };
                            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                                return new RemoteInfo { Uri = response.ResponseUri, Length = -1, Ranges = false };
                        }
                    }
                    throw;
                }
            }
        }

        private void DownloadPart(RemoteInfo info, string path, long start, long end, bool ranged, CancellationToken token)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                token.ThrowIfCancellationRequested();
                long copied = 0;
                HttpWebRequest request = Request(info.Uri);
                if (ranged)
                {
                    request.AddRange(start, end);
                    if (!string.IsNullOrEmpty(info.Validator)) request.Headers[HttpRequestHeader.IfRange] = info.Validator;
                }
                using (token.Register(request.Abort))
                {
                    try
                    {
                        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                        {
                            CheckEncoding(response);
                            long expected = info.Length;
                            if (ranged)
                            {
                                long actualStart, actualEnd, actualTotal;
                                if (response.StatusCode != HttpStatusCode.PartialContent
                                    || !ParseRange(response.Headers[HttpResponseHeader.ContentRange], out actualStart, out actualEnd, out actualTotal)
                                    || actualStart != start || actualEnd != end || actualTotal != info.Length)
                                    throw new InvalidDataException("File đã thay đổi hoặc máy chủ trả sai phần dữ liệu. Hãy tải lại.");
                                expected = end - start + 1;
                            }
                            else if (response.StatusCode != HttpStatusCode.OK)
                                throw new InvalidDataException("Máy chủ không trả về toàn bộ file.");
                            string responseEtag = response.Headers[HttpResponseHeader.ETag];
                            if (info.Validator != null && info.Validator.StartsWith("\"") && responseEtag != null
                                && info.Validator != responseEtag)
                                throw new InvalidDataException("File trên máy chủ đã thay đổi. Hãy tải lại.");
                            if (expected < 0) expected = response.ContentLength;
                            if (expected >= 0 && response.ContentLength >= 0 && response.ContentLength != expected)
                                throw new InvalidDataException("Kích thước phản hồi không khớp phần được yêu cầu.");
                            using (Stream input = response.GetResponseStream())
                            using (FileStream output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
                            {
                                byte[] buffer = new byte[BufferSize];
                                int n;
                                while ((n = input.Read(buffer, 0, buffer.Length)) != 0)
                                {
                                    token.ThrowIfCancellationRequested();
                                    if (expected >= 0 && copied + n > expected)
                                        throw new InvalidDataException("Máy chủ gửi nhiều dữ liệu hơn dự kiến.");
                                    output.Write(buffer, 0, n);
                                    copied += n;
                                    Interlocked.Add(ref received, n);
                                }
                                if (expected >= 0 && copied != expected)
                                    throw new IOException("Kết nối kết thúc trước khi tải xong.");
                            }
                        }
                        return;
                    }
                    catch (Exception error)
                    {
                        WebException webError = error as WebException;
                        if (webError != null && webError.Response != null) webError.Response.Close();
                        token.ThrowIfCancellationRequested();
                        bool retryable = error is WebException || (error is IOException && !(error is InvalidDataException));
                        if (!retryable || attempt == 2) throw;
                        Interlocked.Add(ref received, -copied);
                    }
                }
                if (token.WaitHandle.WaitOne(500 * (attempt + 1))) token.ThrowIfCancellationRequested();
            }
        }
    }
}
