using System;
using System.IO;
using System.Linq;

namespace InspectorArchivos.Services
{
    /// <summary>
    /// Carga una lista de extensiones de imagen/video desde MediaExtensions.txt
    /// y permite comprobar si un nombre de archivo corresponde a una de esas
    /// extensiones. Similar a ExclusionService.
    /// </summary>
    public static class MediaExtensionsService
    {
        private const string FileName = "MediaExtensions.txt";
        private static readonly object _lock = new object();
        private static string[] _exts = Array.Empty<string>();
        private static DateTime _lastLoadUtc = DateTime.MinValue;
        private static bool _loadedOnce;

        public static string FilePath => Path.Combine(AppContext.BaseDirectory, FileName);

        /// <summary>Cuando true, FileEnumerator filtrará para incluir solo estas extensiones.</summary>
        public static bool RestrictToMedia { get; set; } = false;

        public static int Count { get { EnsureLoaded(); return _exts.Length; } }

        public static bool IsMediaExtension(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            EnsureLoaded();
            var ext = Path.GetExtension(fileName)?.TrimStart('.') ?? "";
            if (ext.Length == 0) return false;
            return _exts.Any(e => string.Equals(e.TrimStart('.'), ext, StringComparison.OrdinalIgnoreCase));
        }

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                EnsureFileExists();
                DateTime lastWrite;
                try { lastWrite = File.GetLastWriteTimeUtc(FilePath); }
                catch { lastWrite = DateTime.MinValue; }

                if (!_loadedOnce || lastWrite > _lastLoadUtc)
                {
                    _exts = LoadExtensions(FilePath);
                    _lastLoadUtc = lastWrite;
                    _loadedOnce = true;
                }
            }
        }

        private static string[] LoadExtensions(string path)
        {
            try
            {
                return File.ReadAllLines(path)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && !l.StartsWith("#") && !l.StartsWith(";"))
                    .Select(l => l.StartsWith("*") ? l.TrimStart('*') : l)
                    .Select(l => l.StartsWith(".") ? l.TrimStart('.') : l)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static void EnsureFileExists()
        {
            try
            {
                if (!File.Exists(FilePath))
                    File.WriteAllText(FilePath, DefaultContent, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        private const string DefaultContent =
"# Lista de extensiones de IMAGEN y VIDEO (una por linea).\n# Edita, añade o comenta lineas (con #) para personalizar.\n\n# Imagenes\n.jpg\n.jpeg\n.png\n.gif\n.bmp\n.tif\n.tiff\n.raw\n.heic\n.webp\n.heif\n.psd\n.svg\n\n# Videos\n.mp4\n.mov\n.avi\n.mkv\n.wmv\n.flv\n.webm\n.m4v\n.3gp\n.mpeg\n.mpg\n";
    }
}

