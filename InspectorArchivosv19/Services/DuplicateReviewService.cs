using System;
using System.Collections.Generic;
using System.Linq;
using InspectorArchivos.Database;
using InspectorArchivos.Models;

namespace InspectorArchivos.Services
{
    /// <summary>Lado de procedencia de un archivo en la revisión de duplicados.</summary>
    public enum DuplicateSide { Origen, Destino }

    /// <summary>Resultado del análisis de duplicados: filas ordenadas y estadísticas.</summary>
    public class DuplicateReviewResult
    {
        /// <summary>Filas (una por archivo duplicado) en el orden de visualización.</summary>
        public List<ComparisonRow> Rows { get; } = new List<ComparisonRow>();
        /// <summary>Número de grupos de duplicados detectados.</summary>
        public int GroupCount { get; set; }
        /// <summary>Número total de archivos duplicados mostrados.</summary>
        public int DuplicateFileCount { get; set; }
    }

    /// <summary>
    /// Analiza duplicados por hash BLAKE3 EN MEMORIA (no en SQL), asigna la
    /// "Candidatura" de cada archivo dentro de su grupo (1 = conservar, &gt;1 =
    /// duplicado), persiste el resultado y devuelve las filas listas para revisar.
    ///
    /// Reglas de ordenación dentro de cada grupo:
    ///   1) Fecha de creación ascendente.
    ///   2) Longitud del NOMBRE (Name.Length) ascendente.
    ///   3) Nombre ascendente (OrdinalIgnoreCase).
    ///
    /// Se ignoran los archivos sin hash (null/vacío/espacios). El agrupado por hash
    /// es insensible a mayúsculas. Los grupos de un solo archivo no se muestran.
    /// </summary>
    public class DuplicateReviewService
    {
        private readonly Repository _repo;
        private readonly Action<string> _log;

        public DuplicateReviewService(Repository repo, Action<string> log = null)
        {
            _repo = repo;
            _log = log ?? (_ => { });
        }

        private static bool HasHash(FileRecord f) => !string.IsNullOrWhiteSpace(f?.Hash);

        private static IEnumerable<FileRecord> SortGroup(IEnumerable<FileRecord> files) =>
            files.OrderBy(f => f.CreationTime)
                 .ThenBy(f => (f.Name ?? string.Empty).Length)
                 .ThenBy(f => f.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Ejecuta la revisión. <paramref name="procedencia"/> solo se usa cuando se
        /// incluyen ambos lados: "Origen", "Destino" o "Ambos".
        /// </summary>
        public DuplicateReviewResult Review(long? originScanId, long? destScanId,
            bool includeOrigin, bool includeDest, string procedencia)
        {
            var result = new DuplicateReviewResult();

            var originFiles = includeOrigin && originScanId.HasValue
                ? _repo.GetFilesForScan(originScanId.Value).Where(HasHash).ToList()
                : new List<FileRecord>();
            var destFiles = includeDest && destScanId.HasValue
                ? _repo.GetFilesForScan(destScanId.Value).Where(HasHash).ToList()
                : new List<FileRecord>();

            var updates = new List<(long fileId, int candidatura)>();

            if (includeOrigin && !includeDest)
                BuildSingleSide(originFiles, DuplicateSide.Origen, result, updates);
            else if (includeDest && !includeOrigin)
                BuildSingleSide(destFiles, DuplicateSide.Destino, result, updates);
            else
                BuildBothSides(originFiles, destFiles, procedencia, result, updates);

            // Persistencia: limpia la Candidatura de los escaneos implicados y aplica
            // las nuevas candidaturas en una única transacción.
            //if (includeOrigin && originScanId.HasValue) _repo.ClearCandidaturaForScan(originScanId.Value);
            if (includeOrigin && originScanId.HasValue) _repo.ClearCandidaturaForScanAsync(originScanId.Value);

            //if (includeDest && destScanId.HasValue) _repo.ClearCandidaturaForScan(destScanId.Value);
            if (includeDest && destScanId.HasValue) _repo.ClearCandidaturaForScanAsync(destScanId.Value);

            if (updates.Count > 0) _repo.UpdateCandidaturaBatch(updates);

            return result;
        }

        // CASO 1 (solo origen) y CASO 2 (solo destino): grupos de un único lado.
        private void BuildSingleSide(List<FileRecord> files, DuplicateSide side,
            DuplicateReviewResult result, List<(long, int)> updates)
        {
            var groups = files.GroupBy(f => f.Hash, StringComparer.OrdinalIgnoreCase)
                              .Where(g => g.Count() > 1)
                              .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups)
            {
                result.GroupCount++;
                int c = 1;
                foreach (var f in SortGroup(g))
                    AddRow(f, side, c++, result, updates);
            }
        }

        // CASO 3 (ambos lados): agrupa por hash a través de origen+destino.
        private void BuildBothSides(List<FileRecord> originFiles, List<FileRecord> destFiles,
            string procedencia, DuplicateReviewResult result, List<(long, int)> updates)
        {
            bool ambos = string.Equals(procedencia, "Ambos", StringComparison.OrdinalIgnoreCase);
            bool destinoFirst = string.Equals(procedencia, "destino", StringComparison.OrdinalIgnoreCase);

            var originByHash = originFiles.GroupBy(f => f.Hash, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var destByHash = destFiles.GroupBy(f => f.Hash, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var allHashes = originByHash.Keys.Concat(destByHash.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(h => h, StringComparer.OrdinalIgnoreCase);

            if (ambos)
                _log("Revisar Duplicados (Ambos): se conserva un candidato por lado; los archivos " +
                     "de origen se numeran 1..n y los de destino 1..m de forma independiente " +
                     "(un mismo grupo puede tener dos filas con Candidatura=1).");

            foreach (var h in allHashes)
            {
                originByHash.TryGetValue(h, out var oList); oList ??= new List<FileRecord>();
                destByHash.TryGetValue(h, out var dList); dList ??= new List<FileRecord>();
                if (oList.Count + dList.Count <= 1) continue;

                result.GroupCount++;
                var oSorted = SortGroup(oList).ToList();
                var dSorted = SortGroup(dList).ToList();

                if (ambos)
                {
                    int c = 1;
                    foreach (var f in oSorted) AddRow(f, DuplicateSide.Origen, c++, result, updates);
                    c = 1;
                    foreach (var f in dSorted) AddRow(f, DuplicateSide.Destino, c++, result, updates);
                }
                else
                {
                    // Un único candidato para todo el grupo: el lado prioritario va primero.
                    IEnumerable<(FileRecord f, DuplicateSide side)> ordered = destinoFirst
                        ? dSorted.Select(f => (f, DuplicateSide.Destino)).Concat(oSorted.Select(f => (f, DuplicateSide.Origen)))
                        : oSorted.Select(f => (f, DuplicateSide.Origen)).Concat(dSorted.Select(f => (f, DuplicateSide.Destino)));
                    int c = 1;
                    foreach (var (f, side) in ordered) AddRow(f, side, c++, result, updates);
                }
            }
        }

        private void AddRow(FileRecord f, DuplicateSide side, int candidatura,
            DuplicateReviewResult result, List<(long, int)> updates)
        {
            result.Rows.Add(MakeRow(f, side, candidatura));
            updates.Add((f.Id, candidatura));
            result.DuplicateFileCount++;
        }

        private static ComparisonRow MakeRow(FileRecord f, DuplicateSide side, int candidatura)
        {
            var row = new ComparisonRow
            {
                Nombre = f.Name,
                Extension = f.Extension,
                Hash = f.Hash,
                Fingerprint = f.Fingerprint,
                EsDuplicado = true,
                Candidatura = candidatura,
                NombrePc = f.NombrePc,
                // El #1 se conserva (no se marca); los duplicados (>1) se marcan.
                Selected = candidatura > 1
            };

            if (side == DuplicateSide.Origen)
            {
                row.ProcedenciaArchivo = "origen";
                row.FileIdOrigen = f.Id;
                row.RutaOrigen = f.FullPath;
                row.CarpetaPadreOrigen = f.ParentFolder;
                row.TamanoOrigen = f.Size;
                row.FechaModOrigen = f.LastWriteTime;
                row.FechaCreacionOrigen = f.CreationTime;
                row.AtributosOrigen = f.Attributes;
                row.NombrePcOrigen = f.NombrePc;
                row.HashOrigen = f.Hash;
                row.RevisadoOrigen = f.RevisadoOrigen;
                row.Estado = FileState.DuplicadoOrigen;
            }
            else
            {
                row.ProcedenciaArchivo = "destino";
                row.FileIdDestino = f.Id;
                row.RutaDestino = f.FullPath;
                row.CarpetaPadreDestino = f.ParentFolder;
                row.TamanoDestino = f.Size;
                row.FechaModDestino = f.LastWriteTime;
                row.FechaCreacionDestino = f.CreationTime;
                row.AtributosDestino = f.Attributes;
                row.NombrePcDestino = f.NombrePc;
                row.HashDestino = f.Hash;
                row.RevisadoDestino = f.RevisadoDestino;
                row.Estado = FileState.DuplicadoDestino;
            }
            return row;
        }
    }
}


