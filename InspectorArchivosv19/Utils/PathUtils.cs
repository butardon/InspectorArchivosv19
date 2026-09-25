using System;
using System.IO;

namespace InspectorArchivos.Utils
{
    public static class PathUtils
    {
        public static string NormalizeDir(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            return Path.GetFullPath(path.TrimEnd('\\', '/') + Path.DirectorySeparatorChar);
        }

        public static bool IsValidDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try { return Directory.Exists(path); }
            catch { return false; }
        }
    }

    public static class FormatUtils
    {
        public static string Bytes(long? bytes)
        {
            if (!bytes.HasValue) return "";
            return Bytes(bytes.Value);
        }

        public static string Bytes(long bytes)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB" };
            if (bytes == 0) return "0 B";
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < suf.Length - 1) { v /= 1024; i++; }
            return $"{v:0.##} {suf[i]}";
        }

        public static string Time(TimeSpan ts)
        {
            return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
        }
    }
}

