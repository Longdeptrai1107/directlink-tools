using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DirectLinkShare
{
    internal sealed class ProcessJob : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            public long ProcessTime, JobTime;
            public uint Flags;
            public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr handle, int infoClass, ref ExtendedLimits info, uint size);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr handle, IntPtr process);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
        private IntPtr handle;

        public ProcessJob(Process process)
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            ExtendedLimits limits = new ExtendedLimits(); limits.Basic.Flags = 0x2000; // KILL_ON_JOB_CLOSE
            if (handle == IntPtr.Zero || !SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimits)))
                || !AssignProcessToJobObject(handle, process.Handle))
            {
                int error = Marshal.GetLastWin32Error(); Dispose();
                throw new IOException("Không thể quản lý tiến trình tunnel (Windows " + error + ").");
            }
        }
        public void Dispose() { IntPtr old = Interlocked.Exchange(ref handle, IntPtr.Zero); if (old != IntPtr.Zero) CloseHandle(old); }
    }

    public sealed class TunnelProcess : IDisposable
    {
        public const string Version = "2026.11.1";
        public const string ExpectedHash = "341deda3db5ec498a0482254d6c38c0b7cf01a30c37bb5dd95d2216d08955e35";
        private readonly object gate = new object();
        private readonly object logGate = new object();
        private readonly Queue<string> logs = new Queue<string>();
        private readonly TaskCompletionSource<string> urlReady = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private Process process;
        private ProcessJob job;
        private string temporaryDirectory;
        private int disposed;
        private int failedTcpPrechecks;

        public bool IsAlive
        {
            get { lock (gate) { try { return process != null && !process.HasExited; } catch (InvalidOperationException) { return false; } } }
        }

        public string GetLogText() { lock (logGate) return string.Join(Environment.NewLine, logs.ToArray()); }

        private void Log(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            if (line.Length > 2500) line = line.Substring(0, 2500);
            lock (logGate) { logs.Enqueue(line); while (logs.Count > 100) logs.Dequeue(); }
            Match match = Regex.Match(line, @"https://[a-z0-9]+(?:-[a-z0-9]+)*\.trycloudflare\.com\b", RegexOptions.IgnoreCase);
            if (match.Success) urlReady.TrySetResult(match.Value);
            if (line.IndexOf("Registered tunnel connection", StringComparison.OrdinalIgnoreCase) >= 0) connected.TrySetResult(true);
            if (line.IndexOf("precheck component=\"TCP Connectivity\"", StringComparison.OrdinalIgnoreCase) >= 0
                && line.IndexOf("status=fail", StringComparison.OrdinalIgnoreCase) >= 0
                && Interlocked.Increment(ref failedTcpPrechecks) >= 2)
                connected.TrySetException(new IOException("Mạng hiện tại không kết nối được Cloudflare qua TCP 7844. "
                    + "Hostname đã được cấp nhưng link chưa hoạt động. Hãy kiểm tra tường lửa hoặc dùng mạng cho phép Cloudflare Tunnel rồi thử lại."));
        }

        private string Extract(CancellationToken token)
        {
            string directory = Path.Combine(Path.GetTempPath(), "DirectLinkShare", Guid.NewGuid().ToString("N"));
            lock (gate)
            {
                if (Volatile.Read(ref disposed) != 0) throw new OperationCanceledException(token);
                temporaryDirectory = directory;
                Directory.CreateDirectory(directory);
            }
            string executable = Path.Combine(directory, "cloudflared.exe");
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("DirectLinkShare.cloudflared.gz"))
            {
                if (resource == null) throw new IOException("EXE thiếu thành phần Cloudflare Tunnel được nhúng.");
                using (GZipStream gzip = new GZipStream(resource, CompressionMode.Decompress))
                using (FileStream output = new FileStream(executable, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[262144]; int read;
                    while ((read = gzip.Read(buffer, 0, buffer.Length)) != 0)
                    { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, read); }
                }
            }
            using (SHA256 sha = SHA256.Create())
            using (FileStream input = File.OpenRead(executable))
            {
                string digest = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                if (digest != ExpectedHash) throw new IOException("Kiểm tra SHA-256 của cloudflared không khớp.");
            }
            token.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(directory, "isolated.yml"), "no-autoupdate: true\n");
            return executable;
        }

        public async Task<string> StartAsync(string origin, CancellationToken token)
        {
            try
            {
                Log("Chuẩn bị Cloudflare Tunnel " + Version + " (nhúng trong EXE)...");
                string executable = await Task.Run(() => Extract(token)).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                lock (gate)
                {
                    if (Volatile.Read(ref disposed) != 0) throw new OperationCanceledException(token);
                    ProcessStartInfo info = new ProcessStartInfo(executable);
                    info.Arguments = "tunnel --no-autoupdate --config \"" + Path.Combine(temporaryDirectory, "isolated.yml")
                        + "\" --url " + origin + " --protocol http2 --edge-ip-version 4 --metrics 127.0.0.1:0 --loglevel info";
                    info.WorkingDirectory = temporaryDirectory;
                    info.UseShellExecute = false; info.CreateNoWindow = true;
                    info.RedirectStandardOutput = true; info.RedirectStandardError = true;
                    // Do not pick up a user's named tunnel credentials or global tunnel settings.
                    foreach (string key in new string[] { "TUNNEL_TOKEN", "TUNNEL_TOKEN_FILE", "TUNNEL_NAME", "TUNNEL_ID",
                        "TUNNEL_ORIGIN_CERT", "TUNNEL_CONFIG", "TUNNEL_URL", "TUNNEL_HOSTNAME", "TUNNEL_LOGFILE", "TUNNEL_LOGDIRECTORY" })
                        info.EnvironmentVariables.Remove(key);
                    process = new Process { StartInfo = info, EnableRaisingEvents = true };
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args) { Log(args.Data); };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args) { Log(args.Data); };
                    process.Exited += delegate
                    {
                        IOException error = new IOException("Cloudflare Tunnel đã dừng. Xem nhật ký kết nối để biết chi tiết.");
                        urlReady.TrySetException(error); connected.TrySetException(error);
                    };
                    if (!process.Start()) throw new IOException("Không khởi chạy được cloudflared.");
                    job = new ProcessJob(process);
                    process.BeginOutputReadLine(); process.BeginErrorReadLine();
                }
                using (token.Register(delegate { urlReady.TrySetCanceled(); connected.TrySetCanceled(); StopProcess(); }))
                {
                    Task ready = Task.WhenAll(urlReady.Task, connected.Task);
                    Task winner = await Task.WhenAny(ready, Task.Delay(90000, token)).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (winner != ready) throw new TimeoutException("Chưa kết nối được Cloudflare sau 90 giây. Kiểm tra mạng hoặc tường lửa cho cổng TCP 7844.");
                    await ready.ConfigureAwait(false);
                    return await urlReady.Task.ConfigureAwait(false);
                }
            }
            catch { Dispose(); throw; }
        }

        private void StopProcess()
        {
            lock (gate)
            {
                if (job != null) { job.Dispose(); job = null; }
                if (process != null)
                {
                    try { if (!process.HasExited) process.Kill(); process.WaitForExit(3000); }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            StopProcess();
            lock (gate)
            {
                if (process != null) { process.Dispose(); process = null; }
                if (temporaryDirectory != null)
                {
                    try { Directory.Delete(temporaryDirectory, true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }
}
