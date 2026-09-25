using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Npgsql;
using NpgsqlTypes;
using InspectorArchivos.Models;

namespace InspectorArchivos.Database
{
    /// <summary>
    /// Acceso a datos: inserción de escaneos y archivos, y consultas comparativas.
    /// No usa Entity Framework. Todas las inserciones masivas usan transacciones por lotes.
    /// </summary>
    public class Repository
    {
        private readonly Database _db;
        public Repository(Database db) => _db = db;

        // ---------------------------------------------------------------------
        // Scans
        // ---------------------------------------------------------------------

        public long InsertScan(ScanRecord scan)
        {
            using var tx = _db.BeginTransaction();
            using var cmd = _db.Connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO scans (guid, rootpaths, scope, startedat, finishedat, filecount, foldercount, errorcount, status, notes)
VALUES (@Guid, @RootPaths, @Scope, @StartedAt, @FinishedAt, @FileCount, @FolderCount, @ErrorCount, @Status, @Notes)
RETURNING id;";
            cmd.Parameters.AddWithValue("@Guid", scan.Guid.ToString());
            cmd.Parameters.AddWithValue("@RootPaths", scan.RootPaths ?? "");
            cmd.Parameters.AddWithValue("@Scope", scan.Scope.ToString());
            cmd.Parameters.AddWithValue("@StartedAt", scan.StartedAt); // let Npgsql map type
            cmd.Parameters.AddWithValue("@FinishedAt", scan.FinishedAt == default ? (object)DBNull.Value : scan.FinishedAt  );
            cmd.Parameters.AddWithValue("@FileCount", scan.FileCount);
            cmd.Parameters.AddWithValue("@FolderCount", scan.FolderCount);
            cmd.Parameters.AddWithValue("@ErrorCount", scan.ErrorCount);
            cmd.Parameters.AddWithValue("@Status", scan.Status.ToString());
            cmd.Parameters.AddWithValue("@Notes", (object)scan.Notes ?? DBNull.Value);
            var scalar = cmd.ExecuteScalar();
            if (scalar == null || scalar is DBNull)
                throw new InvalidOperationException("InsertScan did not return a valid id. Check 'scans.id' schema.");
            var id = Convert.ToInt64(scalar);
            tx.Commit();
            scan.Id = id;
            return id;
        }

        public void UpdateScan(ScanRecord scan)
        {
            using var tx = _db.BeginTransaction();
            using var cmd = _db.Connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE scans SET
    finishedat=@FinishedAt, filecount=@FileCount, foldercount=@FolderCount, errorcount=@ErrorCount,
    status=@Status, notes=@Notes
WHERE id=@Id;";
            cmd.Parameters.AddWithValue("@FinishedAt", scan.FinishedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@FileCount", scan.FileCount);
            cmd.Parameters.AddWithValue("@FolderCount", scan.FolderCount);
            cmd.Parameters.AddWithValue("@ErrorCount", scan.ErrorCount);
            cmd.Parameters.AddWithValue("@Status", scan.Status.ToString());
            cmd.Parameters.AddWithValue("@Notes", (object)scan.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Id", scan.Id);
            cmd.ExecuteNonQuery();
            tx.Commit();
        }

        /// <summary>
        /// Número de escaneos válidos (Completed) a conservar por ámbito (Origen/
        /// Destino) tras cada escaneo finalizado correctamente; los más antiguos que
        /// excedan este límite se eliminan de Scans (y, en cascada, de Files y
        /// ScanErrors) para no acumular indefinidamente registros innecesarios.
        /// </summary>
        public const int ScansToKeepPerScope = 4;

        /// <summary>
        /// Elimina de Scans (arrastrando en cascada Files y ScanErrors, por la
        /// FOREIGN KEY ON DELETE CASCADE) los escaneos completados más antiguos del
        /// ámbito indicado, dejando como máximo <paramref name="keep"/> (los de mayor
        /// Id, es decir los más recientes). No afecta a escaneos de otro ámbito ni a
        /// escaneos que no estén en estado Completed (p. ej. Cancelled o Failed).
        /// </summary>
        /// <returns>Número de escaneos eliminados.</returns>
        public int PruneOldScans(ScanScope scope, int keep)
        {
            var ids = new List<long>();
            using (var cmd = _db.Connection.CreateCommand())
            {
                cmd.CommandText = "SELECT id FROM scans WHERE Scope=@Scope AND status=@Status ORDER BY id DESC;";
                cmd.Parameters.AddWithValue("@Scope", scope.ToString());
                cmd.Parameters.AddWithValue("@Status", ScanStatus.Completed.ToString());
                using var r = cmd.ExecuteReader();
                while (r.Read()) ids.Add(r.GetInt64(0));
            }
            if (ids.Count <= keep) return 0;

            var toDelete = ids.Skip(keep).ToList();
            using var tx = _db.BeginTransaction();
            using (var cmd = _db.Connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM scans WHERE id=@Id;";
                var pId = cmd.Parameters.Add("@Id", NpgsqlDbType.Bigint);
                foreach (var id in toDelete)
                {
                    pId.Value = id;
                    cmd.ExecuteNonQuery();
                }
            }
            tx.Commit();
            return toDelete.Count;
        }

        public List<ScanRecord> GetScansByScope(ScanScope scope)
        {
            var list = new List<ScanRecord>();
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM scans WHERE scope=@Scope ORDER BY id DESC;";
            cmd.Parameters.AddWithValue("@Scope", scope.ToString());
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadScan(r));
            return list;
        }

        /// <summary>Devuelve todos los escaneos, del más reciente al más antiguo.</summary>
        public List<ScanRecord> GetAllScans()
        {
            var list = new List<ScanRecord>();
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM scans ORDER BY startedat DESC, id DESC;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadScan(r));
            return list;
        }

        /// <summary>Devuelve todos los errores de escaneo, del último escaneo al más antiguo.</summary>
        public List<ScanErrorRecord> GetAllScanErrors()
        {
            var list = new List<ScanErrorRecord>();
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT id, scanid, Path, error, occurredat FROM scanerrors ORDER BY scanid DESC, id DESC;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new ScanErrorRecord
                {
                    Id = r.GetInt64(0),
                    ScanId = r.GetInt64(1),
                    Path = r.IsDBNull(2) ? null : r.GetString(2),
                    Error = r.GetString(3),
                    OccurredAt = DateTime.Parse(r.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind)
                });
            }
            return list;
        }

        private static ScanRecord ReadScan(NpgsqlDataReader r) => new ScanRecord
        {
            Id = r.GetInt64(r.GetOrdinal("id")),
            Guid = Guid.Parse(r.GetString(r.GetOrdinal("guid"))),
            RootPaths = r.GetString(r.GetOrdinal("rootpaths")),
            Scope = Enum.Parse<ScanScope>(r.GetString(r.GetOrdinal("scope"))),
            StartedAt = DateTime.Parse(r.GetString(r.GetOrdinal("startedat")), null, System.Globalization.DateTimeStyles.RoundtripKind),
            FinishedAt = r.IsDBNull(r.GetOrdinal("finishedat")) ? DateTime.MinValue : DateTime.Parse(r.GetString(r.GetOrdinal("finishedat")), null, System.Globalization.DateTimeStyles.RoundtripKind),
            FileCount = r.GetInt64(r.GetOrdinal("filecount")),
            FolderCount = r.GetInt64(r.GetOrdinal("foldercount")),
            ErrorCount = r.GetInt64(r.GetOrdinal("errorcount")),
            Status = Enum.Parse<ScanStatus>(r.GetString(r.GetOrdinal("status"))),
            Notes = r.IsDBNull(r.GetOrdinal("notes")) ? null : r.GetString(r.GetOrdinal("notes"))
        };

        // ---------------------------------------------------------------------
        // Files (inserción masiva por lotes)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Inserta un lote de archivos dentro de una sola transacción.
        /// </summary>
        public void InsertFileBatch(IEnumerable<FileRecord> files)
        {
            using var tx = _db.BeginTransaction();
            using var cmd = _db.Connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO files (guid, scanid, fullpath, rootpath, name, extension, size,
    creationtime, lastwritetime, lastaccesstime, attributes, hash, fingerprint, scannedat,
    procedencia, nombrepc, revisadoorigen, revisadodestino, tipo_archivo)
VALUES (@Guid, @ScanId, @FullPath, @RootPath, @Name, @Extension, @Size,
    @CreationTime, @LastWriteTime, @LastAccessTime, @Attributes, @Hash, @Fingerprint, @ScannedAt,
    @Procedencia, @NombrePc, @RevisadoOrigen, @RevisadoDestino, @TipoArchivo);";

            var p = new Dictionary<string, NpgsqlParameter>();
            foreach (string name in new[] { "@Guid", "@ScanId", "@FullPath", "@RootPath", "@Name",
                "@Extension", "@Size", "@CreationTime", "@LastWriteTime", "@LastAccessTime",
                "@Attributes", "@Hash", "@Fingerprint", "@ScannedAt", "@Procedencia", "@NombrePc",
                "@RevisadoOrigen", "@RevisadoDestino", "@TipoArchivo" })
            {
                var par = cmd.CreateParameter();
                par.ParameterName = name;
                cmd.Parameters.Add(par);
                p[name] = par;
            }

            foreach (var f in files)
            {
                p["@Guid"].Value = f.Guid.ToString();
                p["@ScanId"].Value = f.ScanId;
                p["@FullPath"].Value = f.FullPath ?? "";
                p["@RootPath"].Value = f.RootPath ?? "";
                p["@Name"].Value = f.Name ?? "";
                p["@Extension"].Value = (object)f.Extension ?? DBNull.Value;
                p["@Size"].Value = f.Size;
                p["@CreationTime"].Value = f.CreationTime.ToString("O");
                p["@LastWriteTime"].Value = f.LastWriteTime.ToString("O");
                p["@LastAccessTime"].Value = f.LastAccessTime.ToString("O");
                p["@Attributes"].Value = (long)f.Attributes;
                p["@Hash"].Value = (object)f.Hash ?? DBNull.Value;
                p["@Fingerprint"].Value = (object)f.Fingerprint ?? DBNull.Value;
                p["@ScannedAt"].Value = f.ScannedAt.ToString("O");
                p["@Procedencia"].Value = (object)f.Procedencia ?? DBNull.Value;
                p["@NombrePc"].Value = f.NombrePc ?? "";
                p["@RevisadoOrigen"].Value = (object)f.RevisadoOrigen ?? DBNull.Value;
                p["@RevisadoDestino"].Value = (object)f.RevisadoDestino ?? DBNull.Value;
                p["@TipoArchivo"].Value = (object)f.TipoArchivo ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        // ---------------------------------------------------------------------
        // Historial (caché de hashes entre escaneos)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Inserta en Historial un lote de archivos, evitando duplicados: solo se
        /// inserta si no existe ya un registro con la misma clave (NombrePc,
        /// FullPath, Size, CreationTime, LastWriteTime, Attributes), gracias a la
        /// restricción UNIQUE de la tabla (INSERT OR IGNORE).
        /// </summary>
        public void InsertHistorialBatch(IEnumerable<FileRecord> files)
        {
            using var tx = _db.BeginTransaction();
            using var cmd = _db.Connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO historial (guid, fullpath, rootpath, name, extension, size,
    creationtime, lastwritetime, lastaccesstime, attributes, hash, fingerprint, scannedat, nombrepc)
VALUES (@Guid, @FullPath, @RootPath, @Name, @Extension, @Size,
    @CreationTime, @LastWriteTime, @LastAccessTime, @Attributes, @Hash, @Fingerprint, @ScannedAt, @NombrePc)
ON CONFLICT DO NOTHING;";

            var p = new Dictionary<string, NpgsqlParameter>();
            foreach (string name in new[] { "@Guid", "@FullPath", "@RootPath", "@Name",
                "@Extension", "@Size", "@CreationTime", "@LastWriteTime", "@LastAccessTime",
                "@Attributes", "@Hash", "@Fingerprint", "@ScannedAt", "@NombrePc" })
            {
                var par = cmd.CreateParameter();
                par.ParameterName = name;
                cmd.Parameters.Add(par);
                p[name] = par;
            }

            foreach (var f in files)
            {
                p["@Guid"].Value = f.Guid.ToString();
                p["@FullPath"].Value = f.FullPath ?? "";
                p["@RootPath"].Value = f.RootPath ?? "";
                p["@Name"].Value = f.Name ?? "";
                p["@Extension"].Value = (object)f.Extension ?? DBNull.Value;
                p["@Size"].Value = f.Size;
                p["@CreationTime"].Value = f.CreationTime.ToString("O");
                p["@LastWriteTime"].Value = f.LastWriteTime.ToString("O");
                p["@LastAccessTime"].Value = f.LastAccessTime.ToString("O");
                p["@Attributes"].Value = (long)f.Attributes;
                p["@Hash"].Value = (object)f.Hash ?? DBNull.Value;
                p["@Fingerprint"].Value = (object)f.Fingerprint ?? DBNull.Value;
                p["@ScannedAt"].Value = f.ScannedAt.ToString("O");
                p["@NombrePc"].Value = f.NombrePc ?? "";
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        /// <summary>
        /// Busca en Historial el hash ya calculado para un archivo idéntico
        /// (misma clave NombrePc+FullPath+Size+CreationTime+LastWriteTime+Attributes)
        /// en un escaneo anterior. Devuelve null si no se encuentra o no tiene hash.
        /// También devuelve, en <paramref name="revisado"/>, el valor del campo
        /// Historial.Revisado ('S', 'N' o vacío/null) del registro encontrado, o
        /// null si no se encontró ningún registro.
        /// </summary>
        public string TryGetHistorialHash(string fullPath, long size,
            DateTime creationTime, DateTime lastWriteTime, out string revisado)
        {
            revisado = null;
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = @"
SELECT hash, revisado FROM historial
WHERE fullpath=@FullPath AND size=@Size
  AND creationtime=@CreationTime AND lastwritetime=@LastWriteTime
LIMIT 1;";
            cmd.Parameters.AddWithValue("@FullPath", fullPath ?? "");
            cmd.Parameters.AddWithValue("@Size", size);
            cmd.Parameters.AddWithValue("@CreationTime", creationTime.ToString("O"));
            cmd.Parameters.AddWithValue("@LastWriteTime", lastWriteTime.ToString("O"));
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            string hash = r.IsDBNull(0) ? null : r.GetString(0);
            revisado = r.IsDBNull(1) ? null : r.GetString(1);
            return string.IsNullOrEmpty(hash) ? null : hash;
        }

        /// <summary>
        /// Marca (UPDATE) el campo Revisado de Historial para el registro identificado
        /// por su clave (NombrePc+FullPath+Size+CreationTime+LastWriteTime+Attributes),
        /// tras confirmar una operación de Copiar/Mover/Eliminar desde el formulario de
        /// confirmación. Operación best-effort: si no existe el registro en Historial
        /// (por ejemplo, porque el archivo nunca se hasheó), no hace nada.
        /// </summary>
        public void UpdateHistorialRevisado(string fullPath, long size,
            DateTime creationTime, DateTime lastWriteTime, string revisado)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = @"
UPDATE historial SET revisado=@Revisado
WHERE fullpath=@FullPath AND size=@Size
  AND creationtime=@CreationTime AND lastwritetime=@LastWriteTime;";
            cmd.Parameters.AddWithValue("@Revisado", (object)revisado ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FullPath", fullPath ?? "");
            cmd.Parameters.AddWithValue("@Size", size);
            cmd.Parameters.AddWithValue("@CreationTime", creationTime.ToString("O"));
            cmd.Parameters.AddWithValue("@LastWriteTime", lastWriteTime.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Elimina de la tabla Files el registro con el Id indicado. Se usa cuando un
        /// archivo se envía a la papelera o se mueve (con la marca de "Revisado"
        /// activada en el formulario de confirmación), para que deje de aparecer en
        /// próximas comparativas. Operación best-effort: si el Id no existe no hace nada.
        /// </summary>
        public void DeleteFile(long id)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM files WHERE id=@Id;";
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
        }

        public void InsertScanError(long scanId, string path, string error)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO scanerrors (scanid, path, error, occurredat) VALUES (@ScanId, @Path, @Error, @OccurredAt);";
            cmd.Parameters.AddWithValue("@ScanId", scanId);
            cmd.Parameters.AddWithValue("@Path", (object)path ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Error", error ?? "");
            cmd.Parameters.AddWithValue("@OccurredAt", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        public long CountFiles(long scanId)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM files WHERE scanid=@ScanId;";
            cmd.Parameters.AddWithValue("@ScanId", scanId);
            return (long)cmd.ExecuteScalar();
        }

        /// <summary>
        /// Número de archivos únicos por contenido (hash + tamaño) en un escaneo.
        /// Los archivos sin hash se cuentan por ruta completa + tamaño.
        /// </summary>
        public long CountUniqueFiles(long scanId)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = @"
SELECT (SELECT COUNT(*) FROM (
    SELECT hash, size FROM files WHERE scanid=@ScanId AND hash IS NOT NULL AND hash<>'' GROUP BY hash, size
))
+ (SELECT COUNT(*) FROM (
    SELECT fullpath, size FROM files WHERE scanid=@ScanId AND (hash IS NULL OR hash='') GROUP BY fullpath, size
));";
            cmd.Parameters.AddWithValue("@ScanId", scanId);
            var r = cmd.ExecuteScalar();
            return r == null || r is DBNull ? 0 : Convert.ToInt64(r);
        }

        // ---------------------------------------------------------------------
        // Comparación origen / destino
        // ---------------------------------------------------------------------

        /// <summary>
        /// Construye la lista comparativa leyendo los archivos de ambos escaneos
        /// y agrupándolos por hash. Usa DataReader (streaming) para no materializar
        /// todo en memoria de golpe; el resultado (filas comparativas) sí se mantiene
        /// en memoria para alimentar la cuadrícula virtual.
        /// </summary>
        public List<ComparisonRow> BuildComparison(long originScanId, long destinationScanId)
        {
            var byHashOrigin = new Dictionary<string, List<FileRecord>>(StringComparer.OrdinalIgnoreCase);
            var byHashDest = new Dictionary<string, List<FileRecord>>(StringComparer.OrdinalIgnoreCase);
            var noHashOrigin = new List<FileRecord>();
            var noHashDest = new List<FileRecord>();

            LoadFilesByHash(originScanId, byHashOrigin, noHashOrigin);
            LoadFilesByHash(destinationScanId, byHashDest, noHashDest);

            var rows = new List<ComparisonRow>();

            var originFiles = new List<FileRecord>();
            foreach (var kv in byHashOrigin) originFiles.AddRange(kv.Value);
            var destFiles = new List<FileRecord>();
            foreach (var kv in byHashDest) destFiles.AddRange(kv.Value);

            // Conteos por clave de contenido (hash + tamaño) para repetición y duplicados.
            var originCountByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in originFiles)
            {
                string k = ContentKey(f);
                if (k == null) continue;
                originCountByKey[k] = originCountByKey.TryGetValue(k, out var c) ? c + 1 : 1;
            }
            var destCountByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in destFiles)
            {
                string k = ContentKey(f);
                if (k == null) continue;
                destCountByKey[k] = destCountByKey.TryGetValue(k, out var c) ? c + 1 : 1;
            }

            // Un contenido está duplicado si aparece más de una vez entre origen+destino.
            var dupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in originCountByKey)
            {
                int tot = kv.Value + (destCountByKey.TryGetValue(kv.Key, out var dc) ? dc : 0);
                if (tot > 1) dupKeys.Add(kv.Key);
            }
            foreach (var kv in destCountByKey)
            {
                if (dupKeys.Contains(kv.Key)) continue;
                if (kv.Value > 1) dupKeys.Add(kv.Key);
            }

            bool IsDup(FileRecord f)
            {
                string k = ContentKey(f);
                return k != null && dupKeys.Contains(k);
            }

            var consumedDest = new HashSet<FileRecord>();

            // Índice de destino por ruta relativa.
            var destByPath = new Dictionary<string, FileRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in destFiles)
                destByPath[RelativePath(d)] = d;

            // Fase A: emparejar por ruta relativa exacta (mismo nombre y ubicación relativa).
            var pendingOrigin = new List<FileRecord>();
            foreach (var o in originFiles)
            {
                if (destByPath.TryGetValue(RelativePath(o), out var d) && !consumedDest.Contains(d))
                {
                    consumedDest.Add(d);
                    rows.Add(RowFromPair(o, d, IsDup(o) || IsDup(d), pathMatch: true,
                        repO: Repetitions(o, originCountByKey),
                        repD: Repetitions(d, destCountByKey)));
                }
                else pendingOrigin.Add(o);
            }

            // Índice de destino por contenido para los no emparejados por ruta.
            var destByKeyAvail = new Dictionary<string, List<FileRecord>>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in destFiles)
            {
                string k = ContentKey(d);
                if (consumedDest.Contains(d) || k == null) continue;
                if (!destByKeyAvail.TryGetValue(k, out var list))
                {
                    list = new List<FileRecord>();
                    destByKeyAvail[k] = list;
                }
                list.Add(d);
            }

            // Fase B: emparejar por contenido (hash + tamaño) los restantes de origen.
            foreach (var o in pendingOrigin)
            {
                FileRecord d = null;
                string k = ContentKey(o);
                if (k != null && destByKeyAvail.TryGetValue(k, out var list) && list.Count > 0)
                {
                    d = list[0];
                    list.RemoveAt(0);
                    consumedDest.Add(d);
                }
                rows.Add(RowFromPair(o, d, IsDup(o) || (d != null && IsDup(d)), pathMatch: false,
                    repO: Repetitions(o, originCountByKey),
                    repD: Repetitions(d, destCountByKey)));
            }

            // Fase C: destino restante sin contrapartida en origen.
            foreach (var d in destFiles)
            {
                if (consumedDest.Contains(d)) continue;
                rows.Add(RowFromPair(null, d, IsDup(d), pathMatch: false,
                    repO: 1,
                    repD: Repetitions(d, destCountByKey)));
            }

            // Archivos sin hash: se comparan por ruta relativa.
            AppendNoHashRows(rows, noHashOrigin, noHashDest);

            return rows;
        }

        private void LoadFilesByHash(long scanId,
            Dictionary<string, List<FileRecord>> byHash,
            List<FileRecord> noHash)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = @"
SELECT id, fullpath, rootpath, name, extension, size, creationtime, lastwritetime,
       lastaccesstime, attributes, hash, fingerprint, tipo_archivo, candidatura, nombrepc, revisadoorigen, revisadodestino
FROM files WHERE scanid=@ScanId ORDER BY hash;";
            cmd.Parameters.AddWithValue("@ScanId", scanId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var f = ReadFileLight(r);
                if (string.IsNullOrEmpty(f.Hash))
                    noHash.Add(f);
                else
                {
                    if (!byHash.TryGetValue(f.Hash, out var list))
                    {
                        list = new List<FileRecord>();
                        byHash[f.Hash] = list;
                    }
                    list.Add(f);
                }
            }
        }

        //private static FileRecord ReadFileLight(NpgsqlDataReader r)
        //{
        //    return new FileRecord
        //    {
        //        Id = r.GetInt64(r.GetOrdinal("id")),
        //        FullPath = r.GetString(r.GetOrdinal("fullpath")),
        //        RootPath = r.GetString(r.GetOrdinal("rootpath")),
        //        Name = r.GetString(r.GetOrdinal("Name")),
        //        Extension = r.IsDBNull(r.GetOrdinal("Extension")) ? null : r.GetString(r.GetOrdinal("Extension")),
        //        Size = r.GetInt64(r.GetOrdinal("Size")),
        //        CreationTime = DateTime.Parse(r.GetString(r.GetOrdinal("creationtime")), null, System.Globalization.DateTimeStyles.RoundtripKind),
        //        LastWriteTime = DateTime.Parse(r.GetString(r.GetOrdinal("lastwritetime")), null, System.Globalization.DateTimeStyles.RoundtripKind),
        //        LastAccessTime = DateTime.Parse(r.GetString(r.GetOrdinal("lastaccesstime")), null, System.Globalization.DateTimeStyles.RoundtripKind),
        //        Attributes = (FileAttributes)Convert.ToInt32(r.GetInt64(r.GetOrdinal("Attributes"))),
        //        Hash = r.IsDBNull(r.GetOrdinal("hash")) ? null : r.GetString(r.GetOrdinal("hash")),
        //        Fingerprint = r.IsDBNull(r.GetOrdinal("fingerprint")) ? null : r.GetString(r.GetOrdinal("fingerprint")),
        //        Candidatura = r.IsDBNull(r.GetOrdinal("candidatura")) ? (int?)null : Convert.ToInt32(r.GetInt64(r.GetOrdinal("candidatura"))),
        //        NombrePc = r.IsDBNull(r.GetOrdinal("nombrepc")) ? "" : r.GetString(r.GetOrdinal("nombrepc")),
        //        RevisadoOrigen = r.IsDBNull(r.GetOrdinal("revisadoorigen")) ? null : r.GetString(r.GetOrdinal("revisadoorigen")),
        //        RevisadoDestino = r.IsDBNull(r.GetOrdinal("revisadodestino")) ? null : r.GetString(r.GetOrdinal("revisadodestino"))
        //    };
        //}

        // csharp
        private static FileRecord ReadFileLight(NpgsqlDataReader r)
        {
            int ordId = r.GetOrdinal("id");
            if (r.IsDBNull(ordId))
                throw new InvalidOperationException("Database returned a file row with NULL id. Check 'files' table integrity.");

            int ordFullPath = r.GetOrdinal("fullpath");
            int ordRootPath = r.GetOrdinal("rootpath");
            int ordName = r.GetOrdinal("name");
            int ordExtension = r.GetOrdinal("extension");
            int ordSize = r.GetOrdinal("size");
            int ordCreation = r.GetOrdinal("creationtime");
            int ordLastWrite = r.GetOrdinal("lastwritetime");
            int ordLastAccess = r.GetOrdinal("lastaccesstime");
            int ordAttributes = r.GetOrdinal("attributes");
            int ordHash = r.GetOrdinal("hash");
            int ordFingerprint = r.GetOrdinal("fingerprint");
            int ordCandidatura = r.GetOrdinal("candidatura");
            int ordNombrePc = r.GetOrdinal("nombrepc");
            int ordRevOrig = r.GetOrdinal("revisadoorigen");
            int ordRevDest = r.GetOrdinal("revisadodestino");
            int ordTipoArchivo = r.GetOrdinal("tipo_archivo");

            return new FileRecord
            {
                Id = r.GetInt64(ordId),
                FullPath = r.GetString(ordFullPath),
                RootPath = r.GetString(ordRootPath),
                Name = r.GetString(ordName),
                Extension = r.IsDBNull(ordExtension) ? null : r.GetString(ordExtension),
                Size = r.GetInt64(ordSize),
                CreationTime = DateTime.Parse(r.GetString(ordCreation), null, System.Globalization.DateTimeStyles.RoundtripKind),
                LastWriteTime = DateTime.Parse(r.GetString(ordLastWrite), null, System.Globalization.DateTimeStyles.RoundtripKind),
                LastAccessTime = DateTime.Parse(r.GetString(ordLastAccess), null, System.Globalization.DateTimeStyles.RoundtripKind),
                Attributes = (FileAttributes)Convert.ToInt32(r.GetInt64(ordAttributes)),
                Hash = r.IsDBNull(ordHash) ? null : r.GetString(ordHash),
                Fingerprint = r.IsDBNull(ordFingerprint) ? null : r.GetString(ordFingerprint),
                Candidatura = r.IsDBNull(ordCandidatura) ? (int?)null : Convert.ToInt32(r.GetInt64(ordCandidatura)),
                NombrePc = r.IsDBNull(ordNombrePc) ? "" : r.GetString(ordNombrePc),
                RevisadoOrigen = r.IsDBNull(ordRevOrig) ? null : r.GetString(ordRevOrig),
                RevisadoDestino = r.IsDBNull(ordRevDest) ? null : r.GetString(ordRevDest),
                TipoArchivo = r.IsDBNull(ordTipoArchivo) ? null : r.GetString(ordTipoArchivo)
            };
        }

        // ---------------------------------------------------------------------
        // Revisar Duplicados
        // ---------------------------------------------------------------------

        /// <summary>
        /// Devuelve todos los archivos de un escaneo (con Id y Candidatura) para el
        /// análisis de duplicados en memoria.
        /// </summary>
        public List<FileRecord> GetFilesForScan(long scanId)
        {
            var list = new List<FileRecord>();
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = @"
SELECT id, fullpath, rootpath, name,extension, size, creationtime, lastwritetime,
       lastaccesstime, attributes, hash, fingerprint, tipo_archivo, candidatura, nombrepc, revisadoorigen, revisadodestino
FROM files WHERE scanid=@ScanId;";
            cmd.Parameters.AddWithValue("@ScanId", scanId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadFileLight(r));
            return list;
        }

        /// <summary>Pone a NULL la Candidatura de todos los archivos de un escaneo.</summary>
        public void ClearCandidaturaForScan(long scanId)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "UPDATE files SET candidatura = NULL WHERE scanid = @s;";
            cmd.Parameters.AddWithValue("@s", scanId);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Asigna la Candidatura a un archivo concreto por Id.</summary>
        public void UpdateCandidatura(long fileId, int candidatura)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "UPDATE files SET candidatura = @c WHERE id = @id;";
            cmd.Parameters.AddWithValue("@c", candidatura);
            cmd.Parameters.AddWithValue("@id", fileId);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Asigna en una sola transacción la Candidatura de varios archivos.</summary>
        public void UpdateCandidaturaBatch(IEnumerable<(long fileId, int candidatura)> updates)
        {
            using var tx = _db.BeginTransaction();
            using var cmd = _db.Connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE files SET candidatura = @c WHERE id = @id;";
            var pc = cmd.CreateParameter(); pc.ParameterName = "@c"; cmd.Parameters.Add(pc);
            var pid = cmd.CreateParameter(); pid.ParameterName = "@id"; cmd.Parameters.Add(pid);
            foreach (var (fileId, candidatura) in updates)
            {
                pc.Value = candidatura;
                pid.Value = fileId;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        /// <summary>Busca en el grupo destino un archivo con el mismo nombre relativo (desde la raíz).</summary>
        private static FileRecord FindCounterpart(FileRecord source, List<FileRecord> group)
        {
            if (group == null) return null;
            string srcRel = RelativePath(source);
            foreach (var f in group)
                if (RelativePath(f).Equals(srcRel, StringComparison.OrdinalIgnoreCase))
                    return f;
            return null;
        }

        private static string RelativePath(FileRecord f)
        {
            if (string.IsNullOrEmpty(f.RootPath) || string.IsNullOrEmpty(f.FullPath)) return f.Name ?? "";
            string full = f.FullPath;
            string root = f.RootPath;
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                string rel = full.Substring(root.Length).TrimStart('\\', '/');
                return rel;
            }
            return f.Name ?? "";
        }

        /// <summary>Clave de contenido: hash BLAKE3 + tamaño exacto. Null si no hay hash.</summary>
        private static string ContentKey(FileRecord f)
        {
            if (f == null || string.IsNullOrEmpty(f.Hash)) return null;
            return f.Hash + "|" + f.Size;
        }

        /// <summary>Número de copias del contenido del archivo en un lado.</summary>
        private static int Repetitions(FileRecord f, Dictionary<string, int> counts)
        {
            if (f == null) return 1;
            string k = ContentKey(f);
            return k != null && counts.TryGetValue(k, out var c) ? c : 1;
        }

        private static ComparisonRow RowFromSingle(FileRecord f, FileState state, bool isOrigin, bool dup, int repetitions)
        {
            var row = new ComparisonRow
            {
                Estado = state,
                Hash = f.Hash,
                HashOrigen = isOrigin ? f.Hash : null,
                HashDestino = isOrigin ? null : f.Hash,
                Fingerprint = f.Fingerprint,
                Nombre = f.Name,
                Extension = f.Extension,
                EsDuplicado = dup,
                NombrePc = f.NombrePc
            };
            if (isOrigin)
            {
                row.RutaOrigen = f.FullPath;
                row.CarpetaPadreOrigen = f.ParentFolder;
                row.TamanoOrigen = f.Size;
                row.FechaModOrigen = f.LastWriteTime;
                row.FechaCreacionOrigen = f.CreationTime;
                row.AtributosOrigen = f.Attributes;
                row.NombrePcOrigen = f.NombrePc;
                row.RepeticionesOrigen = repetitions;
                row.RevisadoOrigen = f.RevisadoOrigen;
                row.FileIdOrigen = f.Id;
            }
            else
            {
                row.RutaDestino = f.FullPath;
                row.CarpetaPadreDestino = f.ParentFolder;
                row.TamanoDestino = f.Size;
                row.FechaModDestino = f.LastWriteTime;
                row.FechaCreacionDestino = f.CreationTime;
                row.AtributosDestino = f.Attributes;
                row.NombrePcDestino = f.NombrePc;
                row.RepeticionesDestino = repetitions;
                row.RevisadoDestino = f.RevisadoDestino;
                row.FileIdDestino = f.Id;
            }
            return row;
        }

        private static ComparisonRow RowFromPair(FileRecord o, FileRecord d, bool esDuplicado, bool pathMatch, int repO, int repD)
        {
            var row = new ComparisonRow
            {
                Hash = (o ?? d)?.Hash,
                HashOrigen = o?.Hash,
                HashDestino = d?.Hash,
                Fingerprint = (o ?? d)?.Fingerprint,
                Nombre = (o ?? d)?.Name,
                Extension = (o ?? d)?.Extension,
                EsDuplicado = esDuplicado,
                RepeticionesOrigen = repO,
                RepeticionesDestino = repD,
                NombrePc = (o ?? d)?.NombrePc
            };

            if (o != null)
            {
                row.RutaOrigen = o.FullPath;
                row.CarpetaPadreOrigen = o.ParentFolder;
                row.TamanoOrigen = o.Size;
                row.FechaModOrigen = o.LastWriteTime;
                row.FechaCreacionOrigen = o.CreationTime;
                row.AtributosOrigen = o.Attributes;
                row.NombrePcOrigen = o.NombrePc;
                row.RevisadoOrigen = o.RevisadoOrigen;
                row.FileIdOrigen = o.Id;
            }
            if (d != null)
            {
                row.RutaDestino = d.FullPath;
                row.CarpetaPadreDestino = d.ParentFolder;
                row.TamanoDestino = d.Size;
                row.FechaModDestino = d.LastWriteTime;
                row.FechaCreacionDestino = d.CreationTime;
                row.AtributosDestino = d.Attributes;
                row.NombrePcDestino = d.NombrePc;
                row.RevisadoDestino = d.RevisadoDestino;
                row.FileIdDestino = d.Id;
            }

            // Determinación del estado
            if (o != null && d != null)
            {
                bool sameName = string.Equals(o.Name, d.Name, StringComparison.OrdinalIgnoreCase);
                bool sameHash = string.Equals(o.Hash, d.Hash, StringComparison.OrdinalIgnoreCase);
                bool sameFp = string.Equals(o.Fingerprint, d.Fingerprint, StringComparison.Ordinal);

                if (pathMatch)
                {
                    // Misma ruta relativa: idéntico (mismo contenido = hash + tamaño) o modificado.
                    bool sameContent = sameHash && o.Size == d.Size;
                    //row.Estado = sameContent ? FileState.Igual : FileState.Modificado;
                    row.Estado = sameContent ? FileState.Igual : FileState.Desconocido;
                   
                }
                else
                {
                    // Emparejado por contenido (hash): la ruta relativa ya no coincide.
                    //if (sameFp) row.Estado = FileState.MovidoORenombrado;
                    //else 
                    if (sameName) row.Estado = FileState.Igual;
                    else row.Estado = FileState.MismoHashDistintoNombre;
                }
            }
            else if (o != null) row.Estado = repO > 1 ? FileState.DuplicadoOrigen : FileState.SoloOrigen;
            else row.Estado = repD > 1 ? FileState.DuplicadoDestino : FileState.SoloDestino;
            if (row.Estado == FileState.Desconocido) row.EsDuplicado = false;
            return row;
        }

        private static void AppendNoHashRows(List<ComparisonRow> rows,
            List<FileRecord> noHashOrigin, List<FileRecord> noHashDest)
        {
            var destByName = new Dictionary<string, FileRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in noHashDest)
                destByName[RelativePath(d)] = d;

            foreach (var o in noHashOrigin)
            {
                destByName.TryGetValue(RelativePath(o), out var d);
                rows.Add(new ComparisonRow
                {
                    //Estado = d == null ? FileState.SoloOrigen : (o.Size == d.Size ? FileState.Igual : FileState.Modificado),
                    Estado = d == null ? FileState.SoloOrigen : (o.Size == d.Size ? FileState.Igual : FileState.Desconocido),
                    Nombre = o.Name,
                    Extension = o.Extension,
                    Hash = null,
                    HashOrigen = null,
                    HashDestino = null,
                    Fingerprint = o.Fingerprint,
                    NombrePc = o.NombrePc ?? d?.NombrePc,
                    RutaOrigen = o.FullPath,
                    CarpetaPadreOrigen = o.ParentFolder,
                    TamanoOrigen = o.Size,
                    FechaModOrigen = o.LastWriteTime,
                    FechaCreacionOrigen = o.CreationTime,
                    AtributosOrigen = o.Attributes,
                    NombrePcOrigen = o.NombrePc,
                    RevisadoOrigen = o.RevisadoOrigen,
                    FileIdOrigen = o.Id,
                    RutaDestino = d?.FullPath,
                    CarpetaPadreDestino = d?.ParentFolder,
                    TamanoDestino = d?.Size,
                    FechaModDestino = d?.LastWriteTime,
                    FechaCreacionDestino = d?.CreationTime,
                    AtributosDestino = d?.Attributes,
                    NombrePcDestino = d?.NombrePc,
                    RevisadoDestino = d?.RevisadoDestino,
                    FileIdDestino = d?.Id
                });
            }
            foreach (var d in noHashDest)
            {
                if (noHashOrigin.Exists(o => RelativePath(o).Equals(RelativePath(d), StringComparison.OrdinalIgnoreCase)))
                    continue;
                rows.Add(RowFromSingle(d, FileState.SoloDestino, false, false, 1));
            }
        }

        // C#
        private static ScanScope ParseScope(string s)
        {
            if (string.IsNullOrEmpty(s)) throw new ArgumentException("Empty scope");
            // Try direct parse (case-insensitive)
            if (Enum.TryParse<ScanScope>(s, ignoreCase: true, out var sc)) return sc;
            // Map known legacy/localized values
            switch (s.Trim().ToLowerInvariant())
            {
                case "origen":   return ScanScope.origen;
                case "destino":  return ScanScope.destino;
                default: throw new ArgumentException($"Unknown scan scope value: '{s}'");
            }
        }

        // C#
        public async Task ClearCandidaturaForScanAsync(long scanId, CancellationToken ct = default)
        {
            await using var conn = new Npgsql.NpgsqlConnection(_db.Connection.ConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE files SET candidatura = NULL WHERE scanid = @s;";
            cmd.Parameters.AddWithValue("@s", scanId);
            cmd.CommandTimeout = 60;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
