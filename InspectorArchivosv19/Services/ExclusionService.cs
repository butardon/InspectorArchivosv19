using System;
using System.IO;
using System.IO.Enumeration;
using System.Linq;

namespace InspectorArchivos.Services
{
    /// <summary>
    /// Carga y aplica la lista de exclusión de archivos y carpetas definida por
    /// el usuario en "Excluir.txt" (junto al ejecutable). Cada entrada de esa
    /// lista se compara a la vez contra nombres de ARCHIVO y de CARPETA: si
    /// coincide con una carpeta, esa carpeta se salta por completo (no se entra
    /// a escanear su contenido); si coincide con un archivo, ese archivo se
    /// omite. Se aplica tanto a los escaneos de Origen/Destino como al
    /// arrastrar y soltar de carpetas, porque ambos pasan por
    /// <see cref="FileEnumerator.Enumerate"/>.
    ///
    /// Si "Excluir.txt" no existe (primera ejecución), se crea automáticamente
    /// con una lista de exclusión recomendada y comentada, para que el usuario
    /// pueda editarla a partir de ahí. Los cambios en el archivo se recogen
    /// automáticamente (se detecta por fecha de modificación) sin reiniciar la
    /// aplicación.
    /// </summary>
    public static class ExclusionService
    {
        private const string ExclusionFileName = "Excluir.txt";

        private static readonly object _lock = new object();
        private static string[] _patterns = Array.Empty<string>();
        private static DateTime _lastLoadUtc = DateTime.MinValue;
        private static bool _loadedOnce;

        /// <summary>Ruta completa de Excluir.txt, junto al ejecutable.</summary>
        public static string FilePath => Path.Combine(AppContext.BaseDirectory, ExclusionFileName);

        /// <summary>Número de patrones de exclusión actualmente cargados (para informar en el log).</summary>
        public static int PatternCount
        {
            get { EnsureLoaded(); return _patterns.Length; }
        }

        /// <summary>
        /// Devuelve true si el nombre indicado (de archivo o de carpeta, SIN
        /// ruta, solo el nombre) coincide con alguna entrada de Excluir.txt.
        /// Se admiten los comodines '*' (cualquier texto) y '?' (un carácter),
        /// sin distinguir mayúsculas/minúsculas.
        /// </summary>
        public static bool IsExcluded(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            EnsureLoaded();
            var patterns = _patterns;
            for (int i = 0; i < patterns.Length; i++)
            {
                if (FileSystemName.MatchesSimpleExpression(patterns[i], name, ignoreCase: true))
                    return true;
            }
            return false;
        }

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                EnsureFileExists();

                DateTime lastWrite;
                try { lastWrite = File.GetLastWriteTimeUtc(FilePath); }
                catch { lastWrite = DateTime.MinValue; }

                // Recarga si es la primera vez o si el archivo ha cambiado desde la
                // última lectura (permite editar Excluir.txt sin reiniciar la app).
                if (!_loadedOnce || lastWrite > _lastLoadUtc)
                {
                    _patterns = LoadPatterns(FilePath);
                    _lastLoadUtc = lastWrite;
                    _loadedOnce = true;
                }
            }
        }

        private static string[] LoadPatterns(string path)
        {
            try
            {
                return File.ReadAllLines(path)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && !l.StartsWith("#") && !l.StartsWith(";"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                // Si no se puede leer (permisos, archivo bloqueado, etc.), se
                // continúa sin exclusiones en vez de interrumpir el escaneo.
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
            catch
            {
                // Si no se puede crear (disco de solo lectura, permisos, etc.), se
                // continúa sin exclusiones; no debe impedir el funcionamiento normal.
            }
        }

        private const string DefaultContent =
@"# InspectorArchivos - Lista de exclusion de archivos y carpetas
# --------------------------------------------------------------
# Un nombre o patron por linea. Las lineas vacias y las que empiezan
# por # o ; se ignoran (son comentarios).
# Se admiten los comodines: * (cualquier texto) y ? (un caracter).
# Cada entrada se compara tanto contra nombres de ARCHIVO como de
# CARPETA: si coincide con una carpeta, esa carpeta se salta por
# completo (no se escanea su contenido); si coincide con un archivo,
# ese archivo se omite de los escaneos y del arrastrar y soltar.
# Puedes anadir, quitar o comentar (con #) lineas: los cambios se
# aplican en el siguiente escaneo, sin reiniciar la aplicacion.

# --- Windows Explorer / miniaturas ---
Thumbs.db
ehthumbs.db
ehthumbs_vista.db
desktop.ini

# --- macOS ---
.DS_Store
._*
.Spotlight-V100
.Trashes
.fseventsd

# --- Sincronizacion (Dropbox, OneDrive, etc.) ---
.syncmetadata
.dropbox
.dropbox.cache

# --- Archivos temporales de Office / LibreOffice ---
~$*
.~lock.*#

# --- Temporales genericos ---
*.tmp
*.temp

# --- Carpetas de sistema ---
System Volume Information
$RECYCLE.BIN
";
    }
}

