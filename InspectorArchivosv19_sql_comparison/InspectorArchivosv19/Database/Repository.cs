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
    /// Acceso a datos: inserción de escaneos y archivos, consultas comparativas y
    /// operaciones de revisión. La comparación origen/destino se ejecuta
    /// directamente en PostgreSQL para evitar cargar millones de archivos en memoria.
    /// </summary>
    public class Repository
    {
        private readonly Database _db;
        public Repository(Database db) => _db = db;

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
            cmd.Parameters.AddWithValue("@StartedAt", scan.StartedAt);
            cmd.Parameters.AddWithValue("@FinishedAt", scan.FinishedAt == default ? (object)DBNull.Value : scan.FinishedAt);
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

        public const int ScansToKeepPerScope = 4;

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

        public List<ScanRecord> GetAllScans()
        {
            var list = new List<ScanRecord>();
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM scans ORDER BY startedat DESC, id DESC;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadScan(r));
            return list;
        }

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
        /// Construye la comparativa directamente en PostgreSQL. No materializa
        /// los archivos de los escaneos en listas C#: PostgreSQL realiza el
        /// emparejamiento por ruta relativa y, para los restantes, por hash+tamaño.
        ///
        /// Los duplicados se obtienen de vw_duplicados_comparacion.
        /// </summary>
        public List<ComparisonRow> BuildComparison(long originScanId, long destinationScanId)
        {
            var rows = new List<ComparisonRow>();

            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandTimeout = 0;
            cmd.CommandText = @"
WITH
origin_base AS (
    SELECT
        f.id, f.fullpath, f.rootpath, f.name, f.extension, f.size,
        f.creationtime, f.lastwritetime, f.attributes, f.hash, f.fingerprint,
        f.nombrepc, f.revisadoorigen, f.revisadodestino,
        CASE
            WHEN length(f.fullpath) >= length(f.rootpath)
             AND lower(substr(f.fullpath, 1, length(f.rootpath))) = lower(f.rootpath)
            THEN ltrim(substr(f.fullpath, length(f.rootpath) + 1), E'\\/')
            ELSE f.name
        END AS relpath,
        COALESCE(d.dup_count, 1)::int AS dup_count
    FROM files f
    LEFT JOIN vw_duplicados_comparacion d
      ON d.scanid = f.scanid
     AND d.procedencia = f.procedencia
     AND d.hash = f.hash
     AND d.size = f.size
    WHERE f.scanid = @OriginScanId
),
dest_base AS (
    SELECT
        f.id, f.fullpath, f.rootpath, f.name, f.extension, f.size,
        f.creationtime, f.lastwritetime, f.attributes, f.hash, f.fingerprint,
        f.nombrepc, f.revisadoorigen, f.revisadodestino,
        CASE
            WHEN length(f.fullpath) >= length(f.rootpath)
             AND lower(substr(f.fullpath, 1, length(f.rootpath))) = lower(f.rootpath)
            THEN ltrim(substr(f.fullpath, length(f.rootpath) + 1), E'\\/')
            ELSE f.name
        END AS relpath,
        COALESCE(d.dup_count, 1)::int AS dup_count
    FROM files f
    LEFT JOIN vw_duplicados_comparacion d
      ON d.scanid = f.scanid
     AND d.procedencia = f.procedencia
     AND d.hash = f.hash
     AND d.size = f.size
    WHERE f.scanid = @DestinationScanId
),
origin_path AS (
    SELECT o.*, row_number() OVER (PARTITION BY relpath ORDER BY id) AS rn
    FROM origin_base o
),
dest_path AS (
    SELECT d.*, row_number() OVER (PARTITION BY relpath ORDER BY id) AS rn
    FROM dest_base d
),
path_pairs AS (
    SELECT o.id AS oid, d.id AS did
    FROM origin_path o
    JOIN dest_path d ON d.relpath = o.relpath AND d.rn = o.rn
),
origin_after_path AS (
    SELECT o.*
    FROM origin_base o
    WHERE NOT EXISTS (SELECT 1 FROM path_pairs p WHERE p.oid = o.id)
),
dest_after_path AS (
    SELECT d.*
    FROM dest_base d
    WHERE NOT EXISTS (SELECT 1 FROM path_pairs p WHERE p.did = d.id)
),
origin_content AS (
    SELECT o.*,
           row_number() OVER (PARTITION BY lower(o.hash), o.size ORDER BY o.id) AS rn_content
    FROM origin_after_path o
    WHERE o.hash IS NOT NULL AND o.hash <> ''
),
dest_content AS (
    SELECT d.*,
           row_number() OVER (PARTITION BY lower(d.hash), d.size ORDER BY d.id) AS rn_content
    FROM dest_after_path d
    WHERE d.hash IS NOT NULL AND d.hash <> ''
),
content_pairs AS (
    SELECT o.id AS oid, d.id AS did
    FROM origin_content o
    JOIN dest_content d
      ON lower(d.hash) = lower(o.hash)
     AND d.size = o.size
     AND d.rn_content = o.rn_content
),
all_pairs AS (
    SELECT oid, did FROM path_pairs
    UNION ALL
    SELECT oid, did FROM content_pairs
),
origin_solo AS (
    SELECT o.id AS oid, NULL::bigint AS did
    FROM origin_base o
    WHERE NOT EXISTS (SELECT 1 FROM all_pairs p WHERE p.oid = o.id)
),
dest_solo AS (
    SELECT NULL::bigint AS oid, d.id AS did
    FROM dest_base d
    WHERE NOT EXISTS (SELECT 1 FROM all_pairs p WHERE p.did = d.id)
),
final_pairs AS (
    SELECT oid, did FROM all_pairs
    UNION ALL
    SELECT oid, did FROM origin_solo
    UNION ALL
    SELECT oid, did FROM dest_solo
)
SELECT
    o.id AS origin_id,
    d.id AS destination_id,
    o.fullpath AS origin_fullpath,
    d.fullpath AS destination_fullpath,
    o.name AS origin_name,
    d.name AS destination_name,
    o.extension AS origin_extension,
    d.extension AS destination_extension,
    o.size AS origin_size,
    d.size AS destination_size,
    o.creationtime AS origin_creationtime,
    d.creationtime AS destination_creationtime,
    o.lastwritetime AS origin_lastwritetime,
    d.lastwritetime AS destination_lastwritetime,
    o.attributes AS origin_attributes,
    d.attributes AS destination_attributes,
    o.hash AS origin_hash,
    d.hash AS destination_hash,
    o.fingerprint AS origin_fingerprint,
    d.fingerprint AS destination_fingerprint,
    o.nombrepc AS origin_nombrepc,
    d.nombrepc AS destination_nombrepc,
    o.revisadoorigen AS origin_revisado,
    d.revisadodestino AS destination_revisado,
    COALESCE(o.dup_count, 1) AS origin_repetitions,
    COALESCE(d.dup_count, 1) AS destination_repetitions,
    CASE
        WHEN o.id IS NULL THEN
            CASE WHEN COALESCE(d.dup_count, 1) > 1
                 THEN 'DuplicadoDestino' ELSE 'SoloDestino' END
        WHEN d.id IS NULL THEN
            CASE WHEN COALESCE(o.dup_count, 1) > 1
                 THEN 'DuplicadoOrigen' ELSE 'SoloOrigen' END
        WHEN COALESCE(o.dup_count, 1) > 1 AND COALESCE(d.dup_count, 1) > 1
            THEN 'DuplicadoAmbos'
        WHEN COALESCE(o.dup_count, 1) > 1
            THEN 'DuplicadoOrigen'
        WHEN COALESCE(d.dup_count, 1) > 1
            THEN 'DuplicadoDestino'
        WHEN o.hash IS NOT NULL
         AND d.hash IS NOT NULL
         AND lower(o.hash) = lower(d.hash)
         AND o.size = d.size
         AND lower(o.name) = lower(d.name)
            THEN 'Igual'
        WHEN o.hash IS NOT NULL
         AND d.hash IS NOT NULL
         AND lower(o.hash) = lower(d.hash)
         AND o.size = d.size
            THEN 'MismoHashDistintoNombre'
        ELSE 'Desconocido'
    END AS estado
FROM final_pairs p
LEFT JOIN origin_base o ON o.id = p.oid
LEFT JOIN dest_base d ON d.id = p.did
ORDER BY
    CASE WHEN o.id IS NULL THEN 1 WHEN d.id IS NULL THEN 2 ELSE 0 END,
    COALESCE(o.relpath, d.relpath),
    COALESCE(o.id, d.id);";

            cmd.Parameters.AddWithValue("@OriginScanId", originScanId);
            cmd.Parameters.AddWithValue("@DestinationScanId", destinationScanId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
                rows.Add(ReadComparisonRow(r));

            return rows;
        }

        private static ComparisonRow ReadComparisonRow(NpgsqlDataReader r)
        {
            var row = new ComparisonRow
            {
                Estado = Enum.Parse<FileState>(r.GetString(r.GetOrdinal("estado"))),
                FileIdOrigen = GetNullableInt64(r, "origin_id"),
                FileIdDestino = GetNullableInt64(r, "destination_id"),
                HashOrigen = GetNullableString(r, "origin_hash"),
                HashDestino = GetNullableString(r, "destination_hash"),
                Hash = GetNullableString(r, "origin_hash") ?? GetNullableString(r, "destination_hash"),
                Fingerprint = GetNullableString(r, "origin_fingerprint") ?? GetNullableString(r, "destination_fingerprint"),
                Nombre = GetNullableString(r, "origin_name") ?? GetNullableString(r, "destination_name"),
                Extension = GetNullableString(r, "origin_extension") ?? GetNullableString(r, "destination_extension"),
                NombrePc = GetNullableString(r, "origin_nombrepc") ?? GetNullableString(r, "destination_nombrepc"),
                RepeticionesOrigen = r.GetInt32(r.GetOrdinal("origin_repetitions")),
                RepeticionesDestino = r.GetInt32(r.GetOrdinal("destination_repetitions"))
            };

            row.EsDuplicado = row.Estado == FileState.DuplicadoOrigen
                || row.Estado == FileState.DuplicadoDestino
                || row.Estado == FileState.DuplicadoAmbos;

            row.RutaOrigen = GetNullableString(r, "origin_fullpath");
            row.CarpetaPadreOrigen = GetParentFolder(row.RutaOrigen);
            row.RutaDestino = GetNullableString(r, "destination_fullpath");
            row.CarpetaPadreDestino = GetParentFolder(row.RutaDestino);

            row.TamanoOrigen = GetNullableInt64(r, "origin_size");
            row.TamanoDestino = GetNullableInt64(r, "destination_size");
            row.FechaCreacionOrigen = ParseNullableDateTime(GetNullableString(r, "origin_creationtime"));
            row.FechaCreacionDestino = ParseNullableDateTime(GetNullableString(r, "destination_creationtime"));
            row.FechaModOrigen = ParseNullableDateTime(GetNullableString(r, "origin_lastwritetime"));
            row.FechaModDestino = ParseNullableDateTime(GetNullableString(r, "destination_lastwritetime"));

            var ao = GetNullableInt64(r, "origin_attributes");
            var ad = GetNullableInt64(r, "destination_attributes");
            row.AtributosOrigen = ao.HasValue ? (FileAttributes)ao.Value : (FileAttributes?)null;
            row.AtributosDestino = ad.HasValue ? (FileAttributes)ad.Value : (FileAttributes?)null;

            row.NombrePcOrigen = GetNullableString(r, "origin_nombrepc");
            row.NombrePcDestino = GetNullableString(r, "destination_nombrepc");
            row.RevisadoOrigen = GetNullableString(r, "origin_revisado");
            row.RevisadoDestino = GetNullableString(r, "destination_revisado");

            return row;
        }

        private static string GetNullableString(NpgsqlDataReader r, string column)
        {
            int ord = r.GetOrdinal(column);
            return r.IsDBNull(ord) ? null : r.GetString(ord);
        }

        private static long? GetNullableInt64(NpgsqlDataReader r, string column)
        {
            int ord = r.GetOrdinal(column);
            return r.IsDBNull(ord) ? (long?)null : r.GetInt64(ord);
        }

        private static DateTime? ParseNullableDateTime(string value)
        {
            return string.IsNullOrEmpty(value)
                ? (DateTime?)null
                : DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);
        }

        private static string GetParentFolder(string path)
        {
            return string.IsNullOrEmpty(path)
                ? string.Empty
                : Path.GetDirectoryName(path) ?? string.Empty;
        }

        // ---------------------------------------------------------------------
        // Revisar Duplicados
        // ---------------------------------------------------------------------

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

        public void ClearCandidaturaForScan(long scanId)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "UPDATE files SET candidatura = NULL WHERE scanid = @s;";
            cmd.Parameters.AddWithValue("@s", scanId);
            cmd.ExecuteNonQuery();
        }

        public void UpdateCandidatura(long fileId, int candidatura)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "UPDATE files SET candidatura = @c WHERE id = @id;";
            cmd.Parameters.AddWithValue("@c", candidatura);
            cmd.Parameters.AddWithValue("@id", fileId);
            cmd.ExecuteNonQuery();
        }

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

        private static ScanScope ParseScope(string s)
        {
            if (string.IsNullOrEmpty(s)) throw new ArgumentException("Empty scope");
            if (Enum.TryParse<ScanScope>(s, ignoreCase: true, out var sc)) return sc;
            switch (s.Trim().ToLowerInvariant())
            {
                case "origen": return ScanScope.origen;
                case "destino": return ScanScope.destino;
                default: throw new ArgumentException($"Unknown scan scope value: '{s}'");
            }
        }

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
