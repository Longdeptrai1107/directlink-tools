using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using DirectLinkDownloader;

internal static class UiWorkflowTests
{
    private static string saved;
    private static string cancelledPath;
    private static T Field<T>(MainForm form, string name)
    {
        return (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Finish(MainForm form)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (Field<bool>(form, "busy"))
        {
            Application.DoEvents();
            if (watch.ElapsedMilliseconds > 30000) throw new TimeoutException("UI download did not finish");
            Thread.Sleep(10);
        }
        Application.DoEvents();
    }

    private static void RunWorkflow(MainForm form, Fixture fixture)
    {
                Assert(SynchronizationContext.Current is WindowsFormsSynchronizationContext, "Windows Forms message loop missing");
                string name = "directlink-ui-" + Guid.NewGuid().ToString("N") + ".bin";
                Field<TextBox>(form, "url").Text = fixture.Address + "/parallel";
                Field<TextBox>(form, "preferredName").Text = name;
                Field<Button>(form, "start").PerformClick();
                Assert(Field<bool>(form, "busy"), "Start button did not start a download");
                Assert(!Field<TextBox>(form, "url").Enabled, "URL stayed editable during download");
                Assert(Field<Button>(form, "cancel").Enabled, "Cancel button not enabled");
                Finish(form);
                saved = Field<string>(form, "lastSavedPath");
                Assert(saved == Path.Combine(DownloadPaths.GetDownloadsFolder(), name), "UI did not save in Windows Downloads");
                using (SHA256 sha = SHA256.Create())
                    Assert(Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(saved))) == Convert.ToBase64String(sha.ComputeHash(fixture.Data)), "UI download content mismatch");
                Assert(Field<Label>(form, "percent").Text == "100%", "UI never reached 100%");
                Assert(Field<Label>(form, "state").Text.StartsWith("Hoàn tất"), "UI success state incorrect");
                Assert(Field<TextBox>(form, "url").Enabled && !Field<Button>(form, "cancel").Enabled,
                    "UI controls after success: form=" + form.Enabled + ", url=" + Field<TextBox>(form, "url").Enabled
                    + ", cancel=" + Field<Button>(form, "cancel").Enabled + ", busy=" + Field<bool>(form, "busy"));
                Console.WriteLine("PASS UI: Start button -> actual 8-thread download -> Downloads -> matching SHA-256 -> 100%");

                string cancelledName = "directlink-cancel-ui-" + Guid.NewGuid().ToString("N") + ".bin";
                cancelledPath = Path.Combine(DownloadPaths.GetDownloadsFolder(), cancelledName);
                Field<TextBox>(form, "url").Text = fixture.Address + "/slow";
                Field<TextBox>(form, "preferredName").Text = cancelledName;
                using (System.Windows.Forms.Timer cancelTimer = new System.Windows.Forms.Timer { Interval = 150 })
                {
                    cancelTimer.Tick += delegate { cancelTimer.Stop(); Field<Button>(form, "cancel").PerformClick(); };
                    cancelTimer.Start();
                    Field<Button>(form, "start").PerformClick();
                    Finish(form);
                }
                Assert(Field<Label>(form, "state").Text == "Đã hủy lượt tải", "Cancel button final state: " + Field<Label>(form, "state").Text);
                Assert(!File.Exists(cancelledPath), "Canceled UI download left a final file");
                Assert(Field<Button>(form, "start").Enabled, "Cannot start again after cancellation");
                Console.WriteLine("PASS UI: Cancel button interrupts transfer, leaves no destination, restores Start");
    }

    [STAThread]
    public static int Main()
    {
        Application.EnableVisualStyles();
        ThreadPool.SetMinThreads(32, 32);
        int result = 0;
        try
        {
            using (Fixture fixture = new Fixture())
            using (MainForm form = new MainForm())
            {
                form.Shown += delegate
                {
                    form.BeginInvoke(new Action(delegate
                    {
                        try { RunWorkflow(form, fixture); }
                        catch (Exception error) { Console.Error.WriteLine(error); result = 1; }
                        finally { form.Close(); }
                    }));
                };
                Application.Run(form);
            }
            return result;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (saved != null && File.Exists(saved)) File.Delete(saved);
            if (cancelledPath != null && File.Exists(cancelledPath)) File.Delete(cancelledPath);
        }
    }
}
