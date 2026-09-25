using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace InspectorArchivos.Services
{
    /// <summary>
    /// Informa del progreso del escaneo a la interfaz.
    /// </summary>
    public sealed class ScanProgress
    {
        public long FilesProcessed { get; set; }
        public string CurrentFile { get; set; }
        public TimeSpan Elapsed { get; set; }
        public long TotalEstimated { get; set; }
        public long Errors { get; set; }
        public string Phase { get; set; } // "Enumerando", "Procesando", "Finalizando"
    }

    /// <summary>
    /// Enumera archivos de forma recursiva con procesamiento asíncrono,
    /// capturando errores de acceso por carpeta o archivo inaccesible.
    /// Es un enumerable diferido: produce archivos a medida que el Scanner los consume.
    /// Aplica la lista de exclusión de <see cref="ExclusionService"/> (Excluir.txt):
    /// las carpetas cuyo nombre coincide se saltan por completo (no se entra a
    /// escanear su contenido) y los archivos cuyo nombre coincide se omiten.
    /// Al ser el único punto de enumeración, esto afecta tanto a los escaneos de
    /// Origen/Destino como al arrastrar y soltar de carpetas.
    /// </summary>
    public static class FileEnumerator
    {
        /// <summary>
        /// Enumera todos los archivos bajo las carpetas raíz indicadas.
        /// Lanza tokens de cancelación y registra errores vía callback.
        /// </summary>
        public static IEnumerable<FileInfo> Enumerate(IEnumerable<string> rootPaths,
            CancellationToken ct, Action<string, Exception> onError,
            Action<DirectoryInfo> onDirectory = null)
        {
            var stack = new Stack<DirectoryInfo>();
            var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in rootPaths)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(root)) continue;
                if (File.Exists(root))
                {
                    var fi = new FileInfo(root);
                    if (!ExclusionService.IsExcluded(fi.Name) && PassesMediaFilter(fi)
                        && emitted.Add(fi.FullName)) yield return fi;
                    continue;
                }
                DirectoryInfo dir;
                try { dir = new DirectoryInfo(root); }
                catch (Exception ex) { onError(root, ex); continue; }
                if (!dir.Exists) { onError(root, new DirectoryNotFoundException(root)); continue; }
                stack.Push(dir);
            }

            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var current = stack.Pop();

                // Notifica cada carpeta visitada (incluida la raíz) para conteo.
                onDirectory?.Invoke(current);

                // Subdirectorios (se descartan los excluidos antes de apilarlos,
                // de forma que no se entra a escanear su contenido en absoluto).
                DirectoryInfo[] subdirs = Array.Empty<DirectoryInfo>();
                try { subdirs = current.GetDirectories(); }
                catch (Exception ex) { onError(current.FullName, ex); }

                foreach (var sd in subdirs)
                {
                    if (ExclusionService.IsExcluded(sd.Name)) continue;
                    stack.Push(sd);
                }

                // Archivos del directorio actual (se omiten los excluidos).
                FileInfo[] files = Array.Empty<FileInfo>();
                try { files = current.GetFiles(); }
                catch (Exception ex) { onError(current.FullName, ex); }

                foreach (var fi in files)
                {
                    ct.ThrowIfCancellationRequested();
                    if (ExclusionService.IsExcluded(fi.Name)) continue;

                    // Si está activado el modo "solo medios" y el archivo NO es
                    // una extension de imagen/video, se omite. MediaExtensionsService
                    // carga MediaExtensions.txt junto al ejecutable.
                    if (!PassesMediaFilter(fi)) continue;

                    if (emitted.Add(fi.FullName)) yield return fi;
                }
            }
        }

        public static bool OnlyNonMedia { get; set; }

        private static bool PassesMediaFilter(FileInfo fi)
        {
            bool media = MediaExtensionsService.IsMediaExtension(fi.Name);
            if (MediaExtensionsService.RestrictToMedia && !media) return false;
            if (OnlyNonMedia && media) return false;
            return true;
        }
    }
}

