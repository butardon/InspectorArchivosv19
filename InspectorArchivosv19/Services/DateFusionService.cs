using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using InspectorArchivos.Database;
using InspectorArchivos.Utils;

namespace InspectorArchivos.Services
{
    /// <summary>Fusiona origen por fecha válida y retira automáticamente los ya presentes en destino.</summary>
    public sealed class DateFusionService
    {
        private static readonly Regex DateFolder = new Regex(@"^\d{4}-\d{2}-\d{2}(?:.*)?$", RegexOptions.Compiled);
        private readonly Repository _repo;

        public DateFusionService(Repository repo) => _repo = repo;

        public DateFusionResult Execute(IEnumerable<string> originEntries, IEnumerable<string> destinationEntries,
            string trashRoot, bool useHash, CancellationToken ct, Action<string> log = null)
        {
            if (string.IsNullOrWhiteSpace(trashRoot)) throw new ArgumentException("Debe indicar la carpeta papelera.", nameof(trashRoot));
            var result = new DateFusionResult { ResultFolder = Path.Combine(trashRoot, "Resultado" + DateTime.Now.ToString("yyyy-MM-dd-HH.mm.ss")) };
            Directory.CreateDirectory(result.ResultFolder);

            var destinations = CollectDestinationFiles(destinationEntries, ct);
            var byName = destinations.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var source in FileEnumerator.Enumerate(originEntries.Distinct(StringComparer.OrdinalIgnoreCase), ct, (_, ex) => log?.Invoke(ex.Message)))
            {
                ct.ThrowIfCancellationRequested();
                long sourceSize = source.Length;
                DateTime creationUtc = source.CreationTimeUtc;
                DateTime modifiedUtc = source.LastWriteTimeUtc;
                DateTime? fechaExif = null; //fecha de toma de la imagen.
                if (MediaExtensionsService.RestrictToMedia)
                    fechaExif = DateUtils.TryGetExifDateTaken(source.Name.ToString());
                DateTime validDate = DateUtils.GetValidModificationDate(source.Name, modifiedUtc, creationUtc, fechaExif, MediaExtensionsService.RestrictToMedia );
                string dayFolder = Path.Combine(result.ResultFolder, validDate.ToString("yyyy-MM-dd"));
                Directory.CreateDirectory(dayFolder);
                string staged = UniquePath(Path.Combine(dayFolder, source.Name));
                File.Move(source.FullName, staged);
                File.SetCreationTimeUtc(staged, creationUtc);
                File.SetLastWriteTimeUtc(staged, modifiedUtc);
                result.Staged++;

                // La marca es siempre de origen y no utiliza NombrePc ni Attributes.
                try { _repo.UpdateHistorialRevisado(source.FullName, sourceSize, creationUtc, validDate, "S"); } catch { }

                bool exists = false;
                if (byName.TryGetValue(source.Name, out var candidates))
                {
                    if (!useHash) exists = candidates.Count > 0;
                    else
                    {
                        string hash = Blake3Hasher.HashFile(staged, ct);
                        exists = candidates.Any(d => string.Equals(hash, Blake3Hasher.HashFile(d.FullName, ct), StringComparison.OrdinalIgnoreCase));
                    }
                }

                if (exists)
                {
                    string moved = UniquePath(Path.Combine(trashRoot, "Movidos", validDate.ToString("yyyy-MM-dd"), Path.GetFileName(staged)));
                    Directory.CreateDirectory(Path.GetDirectoryName(moved));
                    File.Move(staged, moved);
                    File.SetCreationTimeUtc(moved, creationUtc);
                    File.SetLastWriteTimeUtc(moved, modifiedUtc);
                    result.MovedToTrash++;
                }
                else result.PendingReview++;
            }
            return result;
        }

        private static List<FileInfo> CollectDestinationFiles(IEnumerable<string> entries, CancellationToken ct)
        {
            // Solo son válidas las carpetas aaa-mm-dd y aaa-mm-dd[título], incluyendo subcarpetas.
            var allowed = new List<string>();
            foreach (var entry in entries.Where(Directory.Exists))
                foreach (var folder in Directory.EnumerateDirectories(entry, "*", SearchOption.TopDirectoryOnly))
                    if (DateFolder.IsMatch(Path.GetFileName(folder))) allowed.Add(folder);
            return FileEnumerator.Enumerate(allowed, ct, (_, __) => { }).ToList();
        }

        private static string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            int n = 1;
            string candidate;
            do candidate = Path.Combine(dir, $"{name}_{n++}{ext}"); while (File.Exists(candidate));
            return candidate;
        }
    }

    public sealed class DateFusionResult
    {
        public string ResultFolder { get; set; }
        public int Staged { get; set; }
        public int MovedToTrash { get; set; }
        public int PendingReview { get; set; }
    }
}
