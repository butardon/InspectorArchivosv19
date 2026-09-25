using InspectorArchivos.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace InspectorArchivos.Services
{
    /// <summary>
    /// Resultado de una operación de archivo.
    /// </summary>
    public class OperationResult
    {
        public bool Success { get; set; }
        public string Source { get; set; }
        public string Destination { get; set; }
        public string Error { get; set; }
        /// <summary>Aviso no crítico (la operación se completó, pero algo como la preservación de fechas falló).</summary>
        public string Warning { get; set; }
    }

    /// <summary>
    /// Operaciones de copia, movimiento y eliminación (papelera) sobre archivos.
    /// La papelera conserva la ruta relativa de donde procede el archivo.
    /// </summary>
    public class FileOperationsService
    {
        /// <summary>
        /// Nombre de la subcarpeta, dentro de la papelera, donde se guardan los
        /// archivos eliminados (análoga a "Movidos" para la copia de seguridad de
        /// los archivos movidos).
        /// </summary>
        public const string EliminadosSubfolder = "Eliminados";

        /// <summary>
        /// Elimina moviendo el archivo a la papelera, siempre dentro de la subcarpeta
        /// "Eliminados", conservando dentro de ella la ruta relativa de origen.
        /// Si en el destino ya existiera un archivo con el mismo nombre, se versiona
        /// (esquema "_1", "_2", ...) en lugar de sobrescribirlo.
        /// </summary>
        /// <param name="sourcePath">Ruta completa del archivo a eliminar.</param>
        /// <param name="rootPath">Carpeta raíz del escaneo de donde procede (para calcular la relativa).</param>
        /// <param name="trashRoot">Carpeta raíz de la papelera.</param>
        public OperationResult SendToTrash(string sourcePath, string rootPath, string trashRoot)
        {
            try
            {
                if (!File.Exists(sourcePath))
                    return new OperationResult { Success = false, Source = sourcePath, Error = "No existe el archivo." };

                var srcFi = new FileInfo(sourcePath);
                var ts = CaptureTimestamps(srcFi);
                string rel = ComputeRelativePath(sourcePath, rootPath);
                string dest = Path.Combine(trashRoot, EliminadosSubfolder, rel);
                EnsureUniqueAndDirs(dest, versionar: true, out string finalDest);

                File.Move(sourcePath, finalDest);
                if (!ApplyTimestamps(finalDest, ts))
                    return new OperationResult { Success = true, Source = sourcePath, Destination = finalDest, Warning = "Transferido, pero no se pudieron conservar las fechas originales." };
                return new OperationResult { Success = true, Source = sourcePath, Destination = finalDest };
            }
            catch (Exception ex)
            {
                return new OperationResult { Success = false, Source = sourcePath, Error = ex.Message };
            }
        }

        /// <summary>
        /// Copia un archivo a un destino. Si destinationDir es nulo usa la carpeta padre del destino natural.
        /// </summary>
        public OperationResult Copy(string sourcePath, string destinationDir, string preserveRelativeRoot = null, bool versionar = false)
        {
            try
            {
                if (!File.Exists(sourcePath))
                    return new OperationResult { Success = false, Source = sourcePath, Error = "No existe el archivo." };

                var srcFi = new FileInfo(sourcePath);
                var ts = CaptureTimestamps(srcFi);
                string dest = ResolveDestination(sourcePath, destinationDir, preserveRelativeRoot);
                EnsureUniqueAndDirs(dest, versionar, out string finalDest);
                File.Copy(sourcePath, finalDest, overwrite: false);
                if (!ApplyTimestamps(finalDest, ts))
                    return new OperationResult { Success = true, Source = sourcePath, Destination = finalDest, Warning = "Copiado, pero no se pudieron conservar las fechas originales." };
                return new OperationResult { Success = true, Source = sourcePath, Destination = finalDest };
            }
            catch (Exception ex)
            {
                return new OperationResult { Success = false, Source = sourcePath, Error = ex.Message };
            }
        }

        /// <summary>Mueve un archivo al destino.</summary>
        public OperationResult Move(string sourcePath, string destinationDir, string preserveRelativeRoot = null, bool versionar = false)
        {
            try
            {
                if (!File.Exists(sourcePath))
                    return new OperationResult { Success = false, Source = sourcePath, Error = "No existe el archivo." };

                var srcFi = new FileInfo(sourcePath);
                var ts = CaptureTimestamps(srcFi);
                string dest = ResolveDestination(sourcePath, destinationDir, preserveRelativeRoot);
                EnsureUniqueAndDirs(dest, versionar, out string finalDest);
                if (string.Equals(sourcePath, finalDest, StringComparison.OrdinalIgnoreCase))
                    return new OperationResult { Success = true, Source = sourcePath, Destination = finalDest };
                File.Move(sourcePath, finalDest);
                if (!ApplyTimestamps(finalDest, ts))
                    return new OperationResult { Success = true, Source = sourcePath, Destination = finalDest, Warning = "Movido, pero no se pudieron conservar las fechas originales." };
                return new OperationResult { Success = true, Source = sourcePath, Destination = finalDest };
            }
            catch (Exception ex)
            {
                return new OperationResult { Success = false, Source = sourcePath, Error = ex.Message };
            }
        }

        /// <summary>
        /// Calcula, SIN tocar el disco (no crea carpetas, no mueve ni copia nada),
        /// la ruta completa de destino que generaría <see cref="Copy"/> o
        /// <see cref="Move"/> con los mismos parámetros. Se usa para mostrar al
        /// usuario la vista previa de la operación antes de confirmarla. El
        /// resultado asume que el estado del disco no cambia entre la vista previa
        /// y la ejecución real (p. ej. el cálculo de nombre libre por versionado).
        /// </summary>
        public static string PreviewDestination(string sourcePath, string destinationDir, string preserveRelativeRoot = null, bool versionar = false)
        {
            string dest = ResolveDestination(sourcePath, destinationDir, preserveRelativeRoot);
            return PreviewUniqueName(dest, versionar);
        }

        /// <summary>
        /// Calcula, SIN tocar el disco (no crea carpetas, no mueve ni copia nada), la
        /// ruta completa de destino que generaría <see cref="SendToTrash"/> con los
        /// mismos parámetros. Se usa para mostrar la vista previa de la operación de
        /// Eliminar (papelera) en el formulario de confirmación.
        /// </summary>
        public static string PreviewTrashDestination(string sourcePath, string rootPath, string trashRoot)
        {
            string rel = ComputeRelativePath(sourcePath, rootPath);
            string dest = Path.Combine(trashRoot, EliminadosSubfolder, rel);
            return PreviewUniqueName(dest, versionar: true);
        }

        /// <summary>
        /// Igual que la comprobación de nombre libre de <see cref="EnsureUniqueAndDirs"/>
        /// pero sin crear ningún directorio (solo lectura), para uso en vistas previas.
        /// </summary>
        private static string PreviewUniqueName(string destPath, bool versionar)
        {
            if (!File.Exists(destPath)) return destPath;
            return versionar ? NextVersionedName(destPath) : NextNumberedName(destPath);
        }

        // ---------------------------------------------------------------------

        private static string ResolveDestination(string sourcePath, string destinationDir, string preserveRelativeRoot)
        {
            if (string.IsNullOrWhiteSpace(destinationDir))
                destinationDir = Path.GetDirectoryName(sourcePath) ?? "";

            string fileName = Path.GetFileName(sourcePath);

            if (!string.IsNullOrWhiteSpace(preserveRelativeRoot))
            {
                string rel = ComputeRelativePath(sourcePath, preserveRelativeRoot);
                return Path.Combine(destinationDir, rel);
            }
            return Path.Combine(destinationDir, fileName);
        }

        /// <summary>Calcula la ruta relativa de sourcePath respecto a rootPath.</summary>
        public static string ComputeRelativePath(string sourcePath, string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
                return Path.GetFileName(sourcePath);

            string full = Path.GetFullPath(sourcePath);
            string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                string rel = full.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return rel;
            }
            return Path.GetFileName(sourcePath);
        }

        /// <summary>
        /// Captura las fechas originales (creación, modificación, acceso) del archivo en UTC,
        /// para poder restaurarlas en el destino tras una copia/movimiento.
        /// </summary>
        private static FileTimestamps CaptureTimestamps(FileInfo fi)
        {
            try
            {
                fi.Refresh();
                return new FileTimestamps(fi.CreationTimeUtc, fi.LastWriteTimeUtc, fi.LastAccessTimeUtc, true);
            }
            catch
            {
                return default;
            }
        }

        /// <summary>Restaura en el destino las fechas originales del origen. Devuelve false si no fue posible.</summary>
        private static bool ApplyTimestamps(string destPath, FileTimestamps ts)
        {
            if (!ts.Valid) return false;
            try
            {
                var dst = new FileInfo(destPath);
                dst.CreationTimeUtc = ts.CreationUtc;
                dst.LastWriteTimeUtc = ts.LastWriteUtc;
                dst.LastAccessTimeUtc = ts.LastAccessUtc;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private struct FileTimestamps
        {
            public DateTime CreationUtc;
            public DateTime LastWriteUtc;
            public DateTime LastAccessUtc;
            public bool Valid;
            public FileTimestamps(DateTime creation, DateTime lastWrite, DateTime lastAccess, bool valid)
            {
                CreationUtc = creation;
                LastWriteUtc = lastWrite;
                LastAccessUtc = lastAccess;
                Valid = valid;
            }
        }

        /// <summary>
        /// Garantiza que el directorio destino existe y, si ya existe un archivo con el mismo nombre,
        /// genera un nombre libre para no sobrescribir silenciosamente.
        /// Con <paramref name="versionar"/>=true usa el esquema "_1", "_2", ... (continuando la
        /// secuencia si el nombre ya termina en "_N"); en caso contrario usa " (n)".
        /// </summary>
        private static void EnsureUniqueAndDirs(string destPath, bool versionar, out string finalDest)
        {
            string dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            finalDest = destPath;
            if (!File.Exists(destPath)) return;

            finalDest = versionar ? NextVersionedName(destPath) : NextNumberedName(destPath);
        }

        /// <summary>Genera "nombre (n).ext" con el primer n libre.</summary>
        private static string NextNumberedName(string destPath)
        {
            string name = Path.GetFileNameWithoutExtension(destPath);
            string ext = Path.GetExtension(destPath);
            string dirPart = Path.GetDirectoryName(destPath);
            int n = 1;
            while (true)
            {
                string candidate = Path.Combine(dirPart, $"{name} ({n}){ext}");
                if (!File.Exists(candidate)) return candidate;
                n++;
            }
        }

        /// <summary>
        /// Genera "nombre_N.ext" con el primer N libre. Si el nombre ya termina en "_N",
        /// continúa la secuencia desde N+1 (p. ej. "Fichero_1" -> "Fichero_2"); si no,
        /// empieza en "_1" (p. ej. "Fichero" -> "Fichero_1").
        /// </summary>
        private static string NextVersionedName(string destPath)
        {
            string name = Path.GetFileNameWithoutExtension(destPath);
            string ext = Path.GetExtension(destPath);
            string dirPart = Path.GetDirectoryName(destPath);

            string baseName = name;
            BigInteger n = 1;
            var m = System.Text.RegularExpressions.Regex.Match(name, @"^(.*)_(\d+)$");
            if (m.Success)
            {
                baseName = m.Groups[1].Value;
                n = BigInteger.Parse(m.Groups[2].Value) + 1;
            }

            while (true)
            {
                string candidate = Path.Combine(dirPart, $"{baseName}_{n}{ext}");
                if (!File.Exists(candidate)) return candidate;
                n++;
            }
        }
    }
}


