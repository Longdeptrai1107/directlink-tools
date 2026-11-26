using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using DirectLinkShare;

internal static class ShareUiTests
{
    private static T Field<T>(ShareForm form, string name)
    { return (T)typeof(ShareForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form); }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    [STAThread]
    public static int Main()
    {
        Application.EnableVisualStyles();
        string path = Path.Combine(Path.GetTempPath(), "share-ui-test-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "Generated UI test fixture. No user file is selected or published.");
        int result = 0;
        try
        {
            using (ShareForm form = new ShareForm())
            {
                form.Shown += delegate
                {
                    form.BeginInvoke(new Action(delegate
                    {
                        try
                        {
                            Assert(!Field<Button>(form, "create").Enabled && !Field<Button>(form, "copy").Enabled, "Empty share controls incorrect");
                            typeof(ShareForm).GetMethod("SetSelectedFile", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { path });
                            Assert(Field<Button>(form, "create").Enabled, "Selecting a file did not enable Create");
                            Assert(Field<Label>(form, "fileName").Text == Path.GetFileName(path), "Selected filename missing");
                            Field<Button>(form, "create").PerformClick();
                            Assert(Field<bool>(form, "starting") && Field<Button>(form, "stop").Enabled, "Create did not enter starting state");
                            Field<Button>(form, "stop").PerformClick();
                            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                            while (Field<bool>(form, "starting"))
                            {
                                Application.DoEvents(); Thread.Sleep(10);
                                if (DateTime.UtcNow > deadline) throw new TimeoutException("Cancel did not finish");
                            }
                            Assert(!Field<bool>(form, "sharing") && !Field<Button>(form, "copy").Enabled, "Canceled session remained public");
                            Assert(Field<TextBox>(form, "link").Text == "", "Canceled session exposed a stale URL");
                            Assert(Field<Button>(form, "create").Enabled && Field<Button>(form, "choose").Enabled, "UI not reusable after cancellation");
                            using (FileStream writable = File.OpenWrite(path)) { }
                            Console.WriteLine("PASS UI: select file -> Create -> Stop while starting -> no link -> file released -> ready again");
                        }
                        catch (Exception error) { Console.Error.WriteLine(error); result = 1; }
                        finally { form.Close(); }
                    }));
                };
                Application.Run(form);
            }
        }
        finally { File.Delete(path); }
        return result;
    }
}
