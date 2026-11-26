using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using DirectLinkDownloader;
using DirectLinkShare;

internal static class ShareTests
{
    private static int passed;
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Pass(string message) { passed++; Console.WriteLine("PASS " + message); }
    private static string Hash(byte[] data) { using (SHA256 sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(data)); }

    private static HttpWebResponse Response(string url, string method, long? from, long? to)
    {
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = method; request.Timeout = 15000; request.ReadWriteTimeout = 15000;
        request.ServicePoint.ConnectionLimit = 40;
        if (from.HasValue) { if (to.HasValue) request.AddRange(from.Value, to.Value); else request.AddRange(from.Value); }
        return (HttpWebResponse)request.GetResponse();
    }

    private static byte[] Read(HttpWebResponse response)
    {
        using (Stream input = response.GetResponseStream())
        using (MemoryStream output = new MemoryStream()) { input.CopyTo(output); return output.ToArray(); }
    }

    private static void ExpectCode(string url, string method, int expected, long? start)
    {
        bool matched = false;
        try { using (HttpWebResponse response = Response(url, method, start, null)) { matched = (int)response.StatusCode == expected; } }
        catch (WebException error)
        {
            using (HttpWebResponse response = error.Response as HttpWebResponse)
            { matched = response != null && (int)response.StatusCode == expected; }
        }
        Assert(matched, "Expected HTTP " + expected + " for " + method);
    }

    public static int Main(string[] args)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        ThreadPool.SetMinThreads(32, 32);
        string root = Path.Combine(Path.GetTempPath(), "directlink-share-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] content = new byte[3145865]; new Random(721).NextBytes(content);
        string selected = Path.Combine(root, "Bộ cài thử nghiệm.exe"); File.WriteAllBytes(selected, content);
        try
        {
            if (args.Contains("--expect-blocked"))
            {
                using (ShareSession session = new ShareSession())
                {
                    bool correctlyBlocked = false;
                    try
                    {
                        using (CancellationTokenSource timeout = new CancellationTokenSource(45000))
                            session.StartAsync(selected, timeout.Token).GetAwaiter().GetResult();
                    }
                    catch (IOException error)
                    {
                        correctlyBlocked = error.Message.Contains("TCP 7844");
                        Console.WriteLine("Observed expected network restriction: TCP 7844 unreachable.");
                    }
                    Assert(correctlyBlocked, "Expected a precise blocked-egress error in this network");
                    Assert(session.PublicUrl == null && !session.IsAlive, "A failed tunnel returned a URL or remained active");
                    using (FileStream writer = File.OpenWrite(selected)) { }
                    Pass("blocked Internet connection: no usable URL advertised, tunnel closed, file lock released");
                }
            }
            else if (args.Contains("--internet"))
            {
                using (ShareSession session = new ShareSession())
                {
                    try
                    {
                        Console.WriteLine("Starting a real Cloudflare Quick Tunnel for a generated test file only...");
                        using (CancellationTokenSource timeout = new CancellationTokenSource(150000))
                        {
                            string url = session.StartAsync(selected, timeout.Token).GetAwaiter().GetResult();
                            Assert(url.StartsWith("https://") && new Uri(url).Host.EndsWith(".trycloudflare.com"), "Invalid public hostname");
                            using (HttpWebResponse head = Response(url, "HEAD", null, null))
                            { Assert(head.ContentLength == content.Length && head.Headers["Accept-Ranges"] == "bytes", "Public metadata incorrect"); }
                            Pass("real public HTTPS URL: HEAD, correct length, Accept-Ranges");
                            string output = Path.Combine(root, "public-download.bin");
                            DownloadResult result = new DownloadEngine().DownloadAsync(url, output, 8, timeout.Token).GetAwaiter().GetResult();
                            Assert(result.Connections == 8 && Hash(File.ReadAllBytes(output)) == Hash(content), "Public 8-thread content mismatch");
                            Pass("real Internet download: 8 ranges, byte-for-byte SHA-256 match");
                            Assert(session.GetStatistics().BytesSent >= content.Length, "Public transfer counters not updated");
                            session.Dispose(); Assert(!session.IsAlive, "Tunnel kept running after stop");
                            Pass("Stop closes the owned cloudflared process and local origin");
                        }
                    }
                    catch { Console.Error.WriteLine(session.GetLogs()); throw; }
                }
            }
            else
            {
                string oldPath;
                using (LocalFileServer server = new LocalFileServer(selected))
                {
                    string url = server.Origin + server.RelativePath; oldPath = server.RelativePath;
                    using (HttpWebResponse response = Response(url, "GET", null, null))
                    {
                        Assert(response.ContentLength == content.Length && response.Headers["Accept-Ranges"] == "bytes", "GET metadata wrong");
                        Assert(response.Headers["Content-Disposition"].Contains("filename*=UTF-8''"), "UTF-8 filename missing");
                        Assert(Hash(Read(response)) == Hash(content), "GET data mismatch");
                    } Pass("GET streams exact file with UTF-8 attachment name");
                    using (HttpWebResponse response = Response(url, "HEAD", 0, 0))
                    { Assert((int)response.StatusCode == 200 && response.ContentLength == content.Length && Read(response).Length == 0, "HEAD semantics wrong"); }
                    Pass("HEAD reports full length, sends no body, ignores Range");
                    using (HttpWebResponse response = Response(url, "GET", 0, 0))
                    { Assert((int)response.StatusCode == 206 && response.Headers["Content-Range"] == "bytes 0-0/" + content.Length && Read(response)[0] == content[0], "One-byte range wrong"); }
                    Pass("206 one-byte range and exact Content-Range");
                    using (HttpWebResponse response = Response(url, "GET", content.Length - 19, null))
                    { Assert(Read(response).SequenceEqual(content.Skip(content.Length - 19)), "Open-ended range wrong"); }
                    Pass("open-ended byte ranges");
                    using (HttpWebResponse response = Response(url, "GET", -27, null))
                    { Assert(Read(response).SequenceEqual(content.Skip(content.Length - 27)), "Suffix range wrong"); }
                    Pass("suffix byte ranges");
                    ExpectCode(url, "GET", 416, content.Length + 10); Pass("416 for unsatisfiable ranges");
                    ExpectCode(server.Origin + "/", "GET", 404, null);
                    ExpectCode(server.Origin + "/etc/passwd", "GET", 404, null);
                    ExpectCode(server.Origin + server.RelativePath.Replace(".exe", ".txt"), "GET", 404, null);
                    Pass("no directory listing or access to unselected file paths");
                    ExpectCode(url, "POST", 405, null); Pass("rejects non-read methods");
                    HttpWebRequest changed = (HttpWebRequest)WebRequest.Create(url); changed.AddRange(0, 3); changed.Headers["If-Range"] = "\"old-version\"";
                    using (HttpWebResponse response = (HttpWebResponse)changed.GetResponse())
                    { Assert((int)response.StatusCode == 200 && Hash(Read(response)) == Hash(content), "If-Range mismatch should return full file"); }
                    Pass("If-Range mismatch safely returns the full representation");
                    string output = Path.Combine(root, "local-downloaded.bin");
                    DownloadResult result = new DownloadEngine().DownloadAsync(url, output, 8, CancellationToken.None).GetAwaiter().GetResult();
                    Assert(result.Connections == 8 && Hash(File.ReadAllBytes(output)) == Hash(content), "Parallel download mismatch");
                    Pass("existing downloader interoperates using 8 ranges and matching SHA-256");
                    bool immutable = false;
                    try { using (FileStream writer = File.OpenWrite(selected)) { } } catch (IOException) { immutable = true; }
                    Assert(immutable, "File was writable during sharing"); Pass("selected file cannot be modified during a live session");
                    Assert(server.GetStatistics().BytesSent > content.Length && server.GetStatistics().CompletedRequests >= 8, "Stats missing");
                    Pass("actual transmitted-byte and request counters");
                    server.Dispose();
                    bool closed = false;
                    try { using (HttpWebResponse response = Response(url, "GET", null, null)) { } } catch (WebException) { closed = true; }
                    Assert(closed, "Origin survived Stop"); Pass("Stop immediately closes local listening port");
                }
                using (LocalFileServer replacement = new LocalFileServer(selected))
                { Assert(replacement.RelativePath != oldPath, "Public capability token reused"); }
                Pass("new sessions generate new cryptographic file paths");
                string empty = Path.Combine(root, "empty.bin"); File.WriteAllBytes(empty, new byte[0]);
                using (LocalFileServer server = new LocalFileServer(empty))
                {
                    using (HttpWebResponse response = Response(server.Origin + server.RelativePath, "GET", null, null)) Assert(Read(response).Length == 0, "Empty GET failed");
                    ExpectCode(server.Origin + server.RelativePath, "GET", 416, 0);
                } Pass("empty files and unsatisfiable empty-file range");
            }
            Console.WriteLine("ALL " + passed + " SHARE TESTS PASSED"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(root, true); }
    }
}
