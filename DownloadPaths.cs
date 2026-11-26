using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DirectLinkDownloader
{
    public static class DownloadPaths
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);

        public static string GetDownloadsFolder()
        {
            Guid id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            IntPtr pointer = IntPtr.Zero;
            try
            {
                if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out pointer) == 0 && pointer != IntPtr.Zero)
                    return Marshal.PtrToStringUni(pointer);
            }
            finally { if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer); }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        public static string ChooseFileName(string preferred, string disposition, Uri uri)
        {
            if (!string.IsNullOrWhiteSpace(preferred)) return SafeFileName(preferred);
            if (!string.IsNullOrEmpty(disposition))
            {
                Match extended = Regex.Match(disposition, @"(?:^|;)\s*filename\*\s*=\s*(?:""([^""]*)""|([^;]*))", RegexOptions.IgnoreCase);
                if (extended.Success)
                {
                    string value = (extended.Groups[1].Success ? extended.Groups[1].Value : extended.Groups[2].Value).Trim();
                    int first = value.IndexOf('\'');
                    int second = first < 0 ? -1 : value.IndexOf('\'', first + 1);
                    if (second > first && (value.Substring(0, first).Equals("UTF-8", StringComparison.OrdinalIgnoreCase)
                        || value.Substring(0, first).Equals("US-ASCII", StringComparison.OrdinalIgnoreCase)))
                    {
                        try
                        {
                            string name = Uri.UnescapeDataString(value.Substring(second + 1));
                            if (!string.IsNullOrWhiteSpace(name)) return SafeFileName(name);
                        }
                        catch (UriFormatException) { }
                    }
                }
                Match plain = Regex.Match(disposition, @"(?:^|;)\s*filename\s*=\s*(?:""([^""]*)""|([^;]*))", RegexOptions.IgnoreCase);
                if (plain.Success)
                {
                    string name = (plain.Groups[1].Success ? plain.Groups[1].Value : plain.Groups[2].Value).Trim();
                    if (!string.IsNullOrWhiteSpace(name)) return SafeFileName(name);
                }
            }
            string fromUri = uri == null ? "" : uri.AbsolutePath;
            try { fromUri = Uri.UnescapeDataString(fromUri); }
            catch (UriFormatException) { }
            return SafeFileName(fromUri);
        }

        public static string SafeFileName(string name)
        {
            name = (name ?? "").Replace('\\', '/');
            name = name.Substring(name.LastIndexOf('/') + 1);
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim().TrimEnd('.', ' ');
            if (string.IsNullOrWhiteSpace(name)) name = "download.bin";
            if (Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)) name = "_" + name;
            if (name.Length > 170)
            {
                string extension = Path.GetExtension(name);
                if (extension.Length > 20) extension = "";
                name = name.Substring(0, 170 - extension.Length).TrimEnd('.', ' ') + extension;
            }
            return name;
        }

        public static string AvailablePath(string folder, string fileName)
        {
            fileName = SafeFileName(fileName);
            string candidate = Path.Combine(folder, fileName);
            string stem = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            for (int index = 1; File.Exists(candidate) || Directory.Exists(candidate); index++)
            {
                if (index > 10000) throw new IOException("Có quá nhiều file trùng tên trong Downloads.");
                candidate = Path.Combine(folder, stem + " (" + index + ")" + extension);
            }
            return candidate;
        }
    }
}
