using System;
using System.Net;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows.Forms;

[assembly: AssemblyTitle("DirectLink Share")]
[assembly: AssemblyDescription("Share one local file using a Cloudflare Quick Tunnel")]
[assembly: AssemblyProduct("DirectLink Share")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.7.2", FrameworkDisplayName = ".NET Framework 4.7.2")]

namespace DirectLinkShare
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try { Application.Run(new ShareForm()); }
            catch (Exception error) { MessageBox.Show(error.Message, "DirectLink Share", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
