using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using DirectLinkDownloader;

internal sealed class Fixture : IDisposable
{
    private readonly TcpListener listener;
    private readonly Thread acceptThread;
    private volatile bool stopped;
    private int retrySent;
    private int active;
    public int PeakConnections;
    public readonly byte[] Data = new byte[2097291];
    public readonly string Address;

    public Fixture()
    {
        new Random(734).NextBytes(Data);
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Address = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
        acceptThread = new Thread(Accept);
        acceptThread.IsBackground = true;
        acceptThread.Start();
    }

    private void Accept()
    {
        while (!stopped)
        {
            try
            {
                TcpClient client = listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(delegate { Serve(client); });
            }
            catch (SocketException) { if (!stopped) throw; }
            catch (ObjectDisposedException) { return; }
        }
    }

    private void Serve(TcpClient client)
    {
        int current = Interlocked.Increment(ref active);
        int old;
        do { old = PeakConnections; if (old >= current) break; }
        while (Interlocked.CompareExchange(ref PeakConnections, current, old) != old);
        try
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                stream.ReadTimeout = 5000;
                StreamReader reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true);
                string first = reader.ReadLine();
                if (first == null) return;
                string route = first.Split(' ')[1];
                Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string line;
                while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                {
                    int colon = line.IndexOf(':');
                    if (colon > 0) headers[line.Substring(0, colon)] = line.Substring(colon + 1).Trim();
                }
                if (route == "/redirect")
                {
                    WriteHeaders(stream, "302 Found", "Location: " + Address + "/parallel\r\nContent-Length: 0\r\n");
                    return;
                }
                if (route == "/empty")
                {
                    if (headers.ContainsKey("Range")) WriteHeaders(stream, "416 Range Not Satisfiable", "Content-Range: bytes */0\r\nContent-Length: 0\r\n");
                    else WriteHeaders(stream, "200 OK", "Content-Length: 0\r\n");
                    return;
                }
                long start = 0, end = Data.Length - 1;
                bool ranged = headers.ContainsKey("Range") && route != "/single" && route != "/unknown";
                if (ranged)
                {
                    string[] parts = headers["Range"].Substring(6).Split('-');
                    start = long.Parse(parts[0]); end = long.Parse(parts[1]);
                }
                bool probe = ranged && start == 0 && end == 0;
                if (route == "/changed" && !probe)
                {
                    WriteHeaders(stream, "200 OK", "Content-Length: " + Data.Length + "\r\nETag: \"changed\"\r\n");
                    return;
                }
                string extra = "ETag: \"fixture-v1\"\r\n";
                if (route == "/named") extra += "Content-Disposition: attachment; filename*=UTF-8''" + Uri.EscapeDataString("Bộ cài ứng dụng.exe") + "\r\n";
                if (ranged)
                    extra += "Content-Range: bytes " + (route == "/bad" && !probe ? start + 1 : start) + "-" + end + "/" + Data.Length + "\r\n";
                if (route != "/unknown") extra += "Content-Length: " + (end - start + 1) + "\r\n";
                WriteHeaders(stream, ranged ? "206 Partial Content" : "200 OK", extra);
                if (route == "/retry" && !probe && Interlocked.CompareExchange(ref retrySent, 1, 0) == 0)
                {
                    stream.Write(Data, (int)start, (int)((end - start + 1) / 2));
                    return;
                }
                for (long offset = start; offset <= end; offset += 8192)
                {
                    int n = (int)Math.Min(8192, end - offset + 1);
                    stream.Write(Data, (int)offset, n);
                    if (!probe && route == "/slow") Thread.Sleep(15);
                    else if (!probe && (route == "/parallel" || route == "/redirect")) Thread.Sleep(1);
                }
            }
        }
        catch (IOException) { }
        catch (SocketException) { }
        finally { Interlocked.Decrement(ref active); }
    }

    private static void WriteHeaders(Stream stream, string status, string extra)
    {
        byte[] data = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nConnection: close\r\n" + extra + "\r\n");
        stream.Write(data, 0, data.Length);
    }

    public void Dispose()
    {
        stopped = true;
        listener.Stop();
        acceptThread.Join(2000);
    }
}

internal static class DownloaderTests
{
    private static int passed;

    private static void Assert(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static string Hash(byte[] bytes)
    {
        using (SHA256 sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(bytes));
    }

    private static void Success(Fixture fixture, string root, string route, int expectedConnections)
    {
        string path = Path.Combine(root, route.TrimStart('/') + ".bin");
        DownloadEngine engine = new DownloadEngine();
        DownloadResult result = engine.DownloadAsync(fixture.Address + route, path, 8, CancellationToken.None).GetAwaiter().GetResult();
        Assert(result.Connections == expectedConnections, route + ": wrong number of connections");
        Assert(Hash(File.ReadAllBytes(path)) == Hash(fixture.Data), route + ": SHA-256 mismatch");
        Assert(engine.GetProgress().Received == fixture.Data.Length, route + ": progress incorrect after retry");
        Assert(Directory.GetDirectories(root, ".directlink-*").Length == 0, route + ": temp directory leaked");
        passed++;
        Console.WriteLine("PASS " + route + ": byte-for-byte SHA-256 match, " + result.Connections + " connections");
    }

    private static void Reject(Fixture fixture, string root, string route)
    {
        string path = Path.Combine(root, "preserve.bin");
        File.WriteAllText(path, "original file");
        bool rejected = false;
        try { new DownloadEngine().DownloadAsync(fixture.Address + route, path, 8, CancellationToken.None).GetAwaiter().GetResult(); }
        catch (InvalidDataException) { rejected = true; }
        Assert(rejected, route + ": invalid server response accepted");
        Assert(File.ReadAllText(path) == "original file", route + ": previous destination changed");
        Assert(Directory.GetDirectories(root, ".directlink-*").Length == 0, route + ": temp directory leaked");
        passed++;
        Console.WriteLine("PASS " + route + ": rejected, previous destination preserved");
    }

    private static void NamingTests(Fixture fixture, string root)
    {
        DownloadEngine engine = new DownloadEngine();
        DownloadResult first = engine.DownloadToFolderAsync(fixture.Address + "/named", root, null, 8, CancellationToken.None).GetAwaiter().GetResult();
        Assert(Path.GetFileName(first.Path) == "Bộ cài ứng dụng.exe", "UTF-8 server filename not respected");
        Assert(Hash(File.ReadAllBytes(first.Path)) == Hash(fixture.Data), "Automatic download content mismatch");
        passed++; Console.WriteLine("PASS automatic naming: UTF-8 Content-Disposition filename and SHA-256");

        File.WriteAllText(first.Path, "existing download");
        DownloadResult second = engine.DownloadToFolderAsync(fixture.Address + "/named", root, "", 8, CancellationToken.None).GetAwaiter().GetResult();
        Assert(Path.GetFileName(second.Path) == "Bộ cài ứng dụng (1).exe", "Collision not renamed");
        Assert(File.ReadAllText(first.Path) == "existing download", "Automatic mode overwrote old file");
        Assert(Hash(File.ReadAllBytes(second.Path)) == Hash(fixture.Data), "Renamed file content mismatch");
        passed++; Console.WriteLine("PASS collision: auto adds (1), existing file preserved");

        DownloadResult custom = engine.DownloadToFolderAsync(fixture.Address + "/named", root, "custom-setup.exe", 8, CancellationToken.None).GetAwaiter().GetResult();
        Assert(Path.GetFileName(custom.Path) == "custom-setup.exe", "Preferred name ignored");
        passed++; Console.WriteLine("PASS optional custom filename");

        Assert(DownloadPaths.ChooseFileName(null, "attachment; filename=\"setup x64.exe\"", new Uri("https://example.com/get")) == "setup x64.exe", "Quoted filename parsing failed");
        Assert(DownloadPaths.ChooseFileName(null, null, new Uri("https://example.com/app%20setup.exe?token=xyz")) == "app setup.exe", "URL filename parsing failed");
        Assert(DownloadPaths.ChooseFileName(null, null, new Uri("https://example.com/")) == "download.bin", "Empty filename fallback failed");
        Assert(DownloadPaths.SafeFileName("../../outside.exe") == "outside.exe", "Path traversal not removed");
        Assert(DownloadPaths.SafeFileName("CON.exe") == "_CON.exe", "Windows reserved device filename not sanitized");
        Assert(DownloadPaths.SafeFileName("bad:name?.exe") == "bad_name_.exe", "Invalid filename characters not removed");
        Assert(Directory.GetDirectories(root, ".directlink-*").Length == 0, "Automatic mode leaked temporary files");
        passed++; Console.WriteLine("PASS filename safety: quoted names, URL decoding, path traversal, reserved names");

        string downloads = DownloadPaths.GetDownloadsFolder();
        Assert(Path.IsPathRooted(downloads) && Directory.Exists(downloads), "Windows Downloads folder resolution failed");
        passed++; Console.WriteLine("PASS Windows Downloads folder discovery");
    }

    public static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "directlink-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        ThreadPool.SetMinThreads(64, 64);
        try
        {
            using (Fixture fixture = new Fixture())
            {
                Success(fixture, root, "/parallel", 8);
                Assert(fixture.PeakConnections > 1, "Connections were not concurrent");
                Console.WriteLine("Observed concurrent server requests: " + fixture.PeakConnections);
                Success(fixture, root, "/single", 1);
                Success(fixture, root, "/unknown", 1);
                Success(fixture, root, "/retry", 8);
                Success(fixture, root, "/redirect", 8);
                // The second successful download must atomically replace the old file.
                File.WriteAllText(Path.Combine(root, "parallel.bin"), "old content");
                Success(fixture, root, "/parallel", 8);
                Reject(fixture, root, "/bad");
                Reject(fixture, root, "/changed");
                string empty = Path.Combine(root, "empty.bin");
                new DownloadEngine().DownloadAsync(fixture.Address + "/empty", empty, 8, CancellationToken.None).GetAwaiter().GetResult();
                Assert(new FileInfo(empty).Length == 0, "Empty file download failed");
                passed++; Console.WriteLine("PASS /empty: zero-byte file");
                string canceled = Path.Combine(root, "canceled.bin");
                File.WriteAllText(canceled, "keep me");
                using (CancellationTokenSource cts = new CancellationTokenSource(200))
                {
                    bool cancelled = false;
                    try { new DownloadEngine().DownloadAsync(fixture.Address + "/slow", canceled, 1, cts.Token).GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) { cancelled = true; }
                    Assert(cancelled, "Cancellation did not interrupt download");
                }
                Assert(File.ReadAllText(canceled) == "keep me", "Cancellation modified old file");
                Assert(Directory.GetDirectories(root, ".directlink-*").Length == 0, "Cancellation leaked temp files");
                passed++; Console.WriteLine("PASS /slow: canceled, destination preserved, temporary files removed");
                NamingTests(fixture, root);
            }
            Console.WriteLine("ALL " + passed + " TESTS PASSED");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(root, true); }
    }
}
