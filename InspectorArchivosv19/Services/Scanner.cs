using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using InspectorArchivos.Database;
using InspectorArchivos.Models;
using InspectorArchivos.Utils;

namespace InspectorArchivos.Services
{
    /// <summary>
    /// Orquesta un escaneo completo: enumera archivos, calcula BLAKE3,
    /// construye el fingerprint, inserta en SQLite por lotes y reporta progreso.
    /// Procesamiento asíncrono con cancelación.
    /// </summary>
    public class Scanner
    {
        private readonly Repository _repo;
        private readonly int _batchSize;
        private readonly bool _computeHash;

        public Scanner(Repository repo, bool computeHash = true, int batchSize = 2000)
        {
            _repo = repo;
            _computeHash = computeHash;
            _batchSize = batchSize;
        }

        /// <summary>
        /// Ejecuta un escaneo sobre las carpetas raíz indicadas.
        /// </summary>
        /// <param name="rootPaths">Carpetas raíz a escanear.</param>
        /// <param name="scope">Origen o destino.</param>
        /// <param name="progress">Callback de progreso (hilo de UI).</param>
        /// <param name="ct">Token de cancelación.</param>
        /// <param name="onError">Callback de errores de acceso.</param>
        public async Task<ScanRecord> ScanAsync(
            IEnumerable<string> rootPaths,
            ScanScope scope,
            IProgress<ScanProgress> progress,
            CancellationToken ct,
            Action<string, Exception> onError = null)
        {
            // Procedencia según el ámbito del escaneo.
            string procedencia = scope == ScanScope.origen ? "origen" : "destino";
            // Nombre del equipo donde se está ejecutando el escaneo, guardado en cada archivo.
            string nombrePc = Environment.MachineName;
            var roots = new List<string>(rootPaths);
            var scan = new ScanRecord
            {
                Guid = Guid.NewGuid(),
                RootPaths = string.Join("|", roots),
                Scope = scope,
                StartedAt = DateTime.UtcNow,
                Status = ScanStatus.Running
            };
            _repo.InsertScan(scan);

            var sw = Stopwatch.StartNew();
            long processed = 0;
            long errors = 0;
            long total = 0;
            var batch = new List<FileRecord>(_batchSize);
            var now = DateTime.UtcNow;

            // Enumeración y procesamiento en un hilo de fondo.
            await Task.Run(() =>
            {
                try
                {
                    // Fase 1: conteo rápido del total para poder mostrar porcentaje.
                    var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    long folders = 0;
                    progress?.Report(new ScanProgress
                    {
                        FilesProcessed = 0,
                        Elapsed = sw.Elapsed,
                        TotalEstimated = 0,
                        Errors = errors,
                        Phase = "Enumerando"
                    });
                    foreach (var fi in FileEnumerator.Enumerate(roots, ct, (_, __) => { }, d =>
                    {
                        if (seenDirs.Add(d.FullName)) folders++;
                    }))
                    {
                        total++;
                        if (total % 5000 == 0)
                            progress?.Report(new ScanProgress
                            {
                                FilesProcessed = 0,
                                Elapsed = sw.Elapsed,
                                TotalEstimated = total,
                                Errors = errors,
                                Phase = "Enumerando"
                            });
                    }
                    scan.FolderCount = folders;

                    // Fase 2: procesamiento (hash + inserción).
                    foreach (var fi in FileEnumerator.Enumerate(roots, ct, (p, ex) =>
                    {
                        errors++;
                        try { _repo.InsertScanError(scan.Id, p, ex.Message); } catch { }
                        onError?.Invoke(p, ex);
                    }))
                    {
                        ct.ThrowIfCancellationRequested();
                        string root = FindBestRoot(fi.FullName, roots);
                        var record = BuildRecord(fi, scan.Id, now, root);
                        record.Procedencia = procedencia;
                        record.NombrePc = nombrePc;

                        if (_computeHash)
                        {
                            // Antes de recalcular el hash (operación costosa), se consulta
                            // Historial por si este mismo archivo (misma ruta, tamaño,
                            // fechas y atributos, en este mismo equipo) ya fue hasheado en
                            // un escaneo anterior. Si existe, se reutiliza su hash. La misma
                            // consulta devuelve también el valor de Historial.Revisado, que
                            // se traslada a Files.RevisadoOrigen o Files.RevisadoDestino según
                            // el ámbito de este escaneo (si no se encuentra el archivo en
                            // Historial, o su Revisado está vacío, se guarda 'N').
                            string cachedHash = null;
                            string historialRevisado = null;
                            try
                            {
                                cachedHash = _repo.TryGetHistorialHash(record.FullPath, record.Size,
                                    record.CreationTime, record.LastWriteTime, out historialRevisado);
                            }
                            catch { /* si falla la consulta, se recalcula como siempre */ }

                            string revisadoValue = string.IsNullOrEmpty(historialRevisado) ? "N" : historialRevisado;
                            if (scope == ScanScope.origen)
                                record.RevisadoOrigen = revisadoValue;
                            else
                                record.RevisadoDestino = revisadoValue;

                            if (!string.IsNullOrEmpty(cachedHash))
                            {
                                record.Hash = cachedHash;
                            }
                            else
                            {
                                try { record.Hash = Blake3Hasher.HashFile(fi.FullName, ct); }
                                catch (OperationCanceledException) { throw; }
                                catch (Exception ex)
                                {
                                    errors++;
                                    try { _repo.InsertScanError(scan.Id, fi.FullName, "Hash: " + ex.Message); } catch { }
                                    onError?.Invoke(fi.FullName, ex);
                                }
                            }
                        }

                        record.Fingerprint = FileRecord.ComputeFingerprint(record.Size, record.LastWriteTime, record.Hash);

                        batch.Add(record);
                        processed++;

                        if (batch.Count >= _batchSize)
                        {
                            _repo.InsertFileBatch(batch);
                            try { _repo.InsertHistorialBatch(batch); } catch { /* la caché de historial es best-effort */ }
                            batch.Clear();
                        }

                        // Reporte frecuente: primeros archivos y luego cada 25.
                        if (processed <= 50 || processed % 25 == 0)
                        {
                            progress?.Report(new ScanProgress
                            {
                                FilesProcessed = processed,
                                CurrentFile = fi.FullName,
                                Elapsed = sw.Elapsed,
                                TotalEstimated = total,
                                Errors = errors,
                                Phase = "Procesando"
                            });
                        }
                    }

                    if (batch.Count > 0)
                    {
                        _repo.InsertFileBatch(batch);
                        try { _repo.InsertHistorialBatch(batch); } catch { /* la caché de historial es best-effort */ }
                        batch.Clear();
                    }
                }
                catch (OperationCanceledException)
                {
                    scan.Status = ScanStatus.Cancelled;
                }
            }, ct).ConfigureAwait(true);

            scan.FileCount = processed;
            scan.ErrorCount = errors;
            scan.FinishedAt = DateTime.UtcNow;
            if (scan.Status != ScanStatus.Cancelled)
                scan.Status = ScanStatus.Completed;

            _repo.UpdateScan(scan);

            progress?.Report(new ScanProgress
            {
                FilesProcessed = processed,
                CurrentFile = "",
                Elapsed = sw.Elapsed,
                TotalEstimated = total > 0 ? total : processed,
                Errors = errors,
                Phase = "Finalizado"
            });

            return scan;
        }

        private static FileRecord BuildRecord(FileInfo fi, long scanId, DateTime scannedAt, string rootPath)
        {

            DateTime? fechaExif = null; //fecha de toma de la imagen.
            if (MediaExtensionsService.RestrictToMedia)
                fechaExif = DateUtils.TryGetExifDateTaken(fi.FullName.ToString());
            return new FileRecord
            {
                Guid = Guid.NewGuid(),
                ScanId = scanId,
                FullPath = fi.FullName,
                RootPath = rootPath ?? (fi.Directory?.FullName ?? ""),
                Name = fi.Name,
                Extension = fi.Extension,
                Size = fi.Length,
                CreationTime = fi.CreationTimeUtc,
                // Fecha de modificación "válida": menor entre la detectada en el nombre
                // (si es válida) y la fecha EXIF/última escritura del archivo.                
                LastWriteTime = DateUtils.GetValidModificationDate(fi.Name, fi.LastWriteTimeUtc, fi.CreationTimeUtc, fechaExif, MediaExtensionsService.RestrictToMedia),
                LastAccessTime = fi.LastAccessTimeUtc,
                Attributes = fi.Attributes,
                ScannedAt = scannedAt
            };
        }

        /// <summary>Devuelve la carpeta raíz de escaneo a la que pertenece la ruta.</summary>
        private static string FindBestRoot(string fullPath, List<string> roots)
        {
            string best = null;
            int bestLen = -1;
            foreach (var r in roots)
            {
                if (string.IsNullOrEmpty(r)) continue;
                if (fullPath.StartsWith(r, StringComparison.OrdinalIgnoreCase) && r.Length > bestLen)
                {
                    best = r;
                    bestLen = r.Length;
                }
            }
            return best;
        }
    }
}

