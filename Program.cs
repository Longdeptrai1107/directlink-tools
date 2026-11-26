using System;
using System.Net;
using System.Windows.Forms;
using System.Reflection;
using System.Runtime.Versioning;

[assembly: AssemblyTitle("DirectLink Download Manager")]
[assembly: AssemblyDescription("Portable multi-threaded downloader")]
[assembly: AssemblyProduct("DirectLink")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.7.2", FrameworkDisplayName = ".NET Framework 4.7.2")]

namespace DirectLinkDownloader
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Application.Run(new MainForm()); }
            catch (Exception error) { MessageBox.Show(error.Message, "DirectLink", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
