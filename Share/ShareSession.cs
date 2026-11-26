using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace DirectLinkShare
{
    public sealed class ShareSession : IDisposable
    {
        private LocalFileServer server;
        private readonly TunnelProcess tunnel = new TunnelProcess();
        private int disposed;
        public string PublicUrl { get; private set; }
        public bool IsAlive { get { return Volatile.Read(ref disposed) == 0 && tunnel.IsAlive; } }
        public string GetLogs() { return tunnel.GetLogText(); }
        public ShareStatistics GetStatistics() { return server == null ? new ShareStatistics() : server.GetStatistics(); }

        public async Task<string> StartAsync(string path, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                server = new LocalFileServer(path);
                string hostname = await tunnel.StartAsync(server.Origin, token).ConfigureAwait(false);
                string link = hostname + server.RelativePath;
                // Only enable Copy when the actual public file URL responds correctly.
                await CheckPublicAsync(link, server.Length, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                PublicUrl = link;
                return link;
            }
            catch { Dispose(); throw; }
        }

        private static async Task CheckPublicAsync(string url, long expectedLength, CancellationToken token)
        {
            Exception last = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                token.ThrowIfCancellationRequested();
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "HEAD"; request.Timeout = 10000; request.ReadWriteTimeout = 10000;
                request.UserAgent = "DirectLinkShare/1.0";
                using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    deadline.CancelAfter(10000);
                    using (deadline.Token.Register(request.Abort))
                    {
                    try
                    {
                        using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                        {
                            if (response.StatusCode != HttpStatusCode.OK || response.ContentLength != expectedLength
                                || response.Headers["Accept-Ranges"] != "bytes") throw new IOException("Link công khai chưa trả đúng file.");
                            return;
                        }
                    }
                    catch (WebException error)
                    { if (error.Response != null) error.Response.Close(); token.ThrowIfCancellationRequested(); last = error; }
                    catch (IOException error) { last = error; }
                    }
                }
                await Task.Delay(1000 + attempt * 500, token).ConfigureAwait(false);
            }
            throw new IOException("Tunnel đã khởi động nhưng chưa truy cập được file từ Internet. Hãy thử lại. " + (last == null ? "" : last.Message), last);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            // Stop serving immediately, then close the tunnel process we own.
            if (server != null) server.Dispose();
            tunnel.Dispose();
            PublicUrl = null;
        }
    }
}
