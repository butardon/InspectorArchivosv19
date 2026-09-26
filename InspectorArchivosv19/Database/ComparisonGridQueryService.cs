using System;
using System.Collections.Generic;
using System.Globalization;
using InspectorArchivos.Models;
using Npgsql;

namespace InspectorArchivos.Database
{
    /// <summary>
    /// Filtrado y ordenación SQL de la comparativa ya construida.
    /// Mantiene la lista _allRows para las operaciones existentes, pero evita que
    /// ApplyFilterAndSort recorra y ordene millones de objetos en C#.
    /// </summary>
    public sealed class ComparisonGridQueryService
    {
        private readonly NpgsqlConnection _connection;
        public ComparisonGridQueryService(NpgsqlConnection connection) => _connection = connection ?? throw new ArgumentNullException(nameof(connection));

        public void EnsureSchema()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandTimeout = 0;
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS public.comparison_grid_cache (
 origin_scan_id bigint NOT NULL,
 destination_scan_id bigint NOT NULL,
 row_id bigint NOT NULL,
 origin_id bigint NULL,
 destination_id bigint NULL,
 has_origin boolean NOT NULL,
 has_destination boolean NOT NULL,
 estado text NOT NULL,
 origin_fullpath text NULL,
 destination_fullpath text NULL,
 origin_name text NULL,
 destination_name text NULL,
 origin_extension text NULL,
 destination_extension text NULL,
 origin_size bigint NULL,
 destination_size bigint NULL,
 origin_creationtime text NULL,
 destination_creationtime text NULL,
 origin_lastwritetime text NULL,
 destination_lastwritetime text NULL,
 origin_attributes bigint NULL,
 destination_attributes bigint NULL,
 origin_hash text NULL,
 destination_hash text NULL,
 origin_fingerprint text NULL,
 destination_fingerprint text NULL,
 origin_nombrepc text NULL,
 destination_nombrepc text NULL,
 origin_revisado text NULL,
 destination_revisado text NULL,
 origin_repetitions integer NOT NULL,
 destination_repetitions integer NOT NULL,
 origin_candidatura bigint NULL,
 destination_candidatura bigint NULL,
 nombre text NULL,
 extension text NULL,
 fingerprint text NULL,
 es_duplicado boolean NOT NULL,
 carpeta_padre_origen text NULL,
 carpeta_padre_destino text NULL,
 PRIMARY KEY (origin_scan_id, destination_scan_id, row_id)
);
CREATE INDEX IF NOT EXISTS ix_cgc_presence ON public.comparison_grid_cache (origin_scan_id, destination_scan_id, has_origin, has_destination, row_id);
CREATE INDEX IF NOT EXISTS ix_cgc_estado ON public.comparison_grid_cache (origin_scan_id, destination_scan_id, estado, row_id);
CREATE INDEX IF NOT EXISTS ix_cgc_or_name ON public.comparison_grid_cache (origin_scan_id, destination_scan_id, origin_name, row_id);
CREATE INDEX IF NOT EXISTS ix_cgc_de_name ON public.comparison_grid_cache (origin_scan_id, destination_scan_id, destination_name, row_id);
CREATE INDEX IF NOT EXISTS ix_cgc_or_path ON public.comparison_grid_cache (origin_scan_id, destination_scan_id, origin_fullpath, row_id);
CREATE INDEX IF NOT EXISTS ix_cgc_de_path ON public.comparison_grid_cache (origin_scan_id, destination_scan_id, destination_fullpath, row_id);
CREATE INDEX IF NOT EXISTS ix_cgc_or_size ON public.comparison_grid_cache (origin_scan_id, destination_scan_id, origin_size, row_id);
CREATE INDEX IF NOT EXISTS ix_cgc_de_size ON public.comparison_grid_cache (origin_scan_id, destination_scan_id, destination_size, row_id);
CREATE INDEX IF NOT EXISTS ix_cgc_or_date ON public.comparison_grid_cache (origin_scan_id, destination_scan_id, origin_lastwritetime, row_id);
CREATE INDEX IF NOT EXISTS ix_cgc_de_date ON public.comparison_grid_cache (origin_scan_id, destination_scan_id, destination_lastwritetime, row_id);
ANALYZE public.comparison_grid_cache;";
            cmd.ExecuteNonQuery();
        }

        public void RebuildCache(long originScanId, long destinationScanId, IList<ComparisonRow> rows)
        {
            EnsureSchema();
            using (var del = _connection.CreateCommand())
            {
                del.CommandTimeout = 0;
                del.CommandText = "DELETE FROM public.comparison_grid_cache WHERE origin_scan_id=@o AND destination_scan_id=@d;";
                del.Parameters.AddWithValue("@o", originScanId);
                del.Parameters.AddWithValue("@d", destinationScanId);
                del.ExecuteNonQuery();
            }

            using var importer = _connection.BeginBinaryImport(@"
COPY public.comparison_grid_cache
(origin_scan_id,destination_scan_id,row_id,origin_id,destination_id,has_origin,has_destination,estado,
 origin_fullpath,destination_fullpath,origin_name,destination_name,origin_extension,destination_extension,
 origin_size,destination_size,origin_creationtime,destination_creationtime,origin_lastwritetime,destination_lastwritetime,
 origin_attributes,destination_attributes,origin_hash,destination_hash,origin_fingerprint,destination_fingerprint,
 origin_nombrepc,destination_nombrepc,origin_revisado,destination_revisado,origin_repetitions,destination_repetitions,
 origin_candidatura,destination_candidatura,nombre,extension,fingerprint,es_duplicado,carpeta_padre_origen,carpeta_padre_destino)
FROM STDIN (FORMAT BINARY)");

            long id = 0;
            foreach (var r in rows)
            {
                id++;
                importer.StartRow();
                importer.Write(originScanId); importer.Write(destinationScanId); importer.Write(id);
                Write(importer, r.FileIdOrigen); Write(importer, r.FileIdDestino);
                importer.Write(!string.IsNullOrEmpty(r.RutaOrigen)); importer.Write(!string.IsNullOrEmpty(r.RutaDestino));
                importer.Write(r.EstadoLabel);
                Write(importer, r.RutaOrigen); Write(importer, r.RutaDestino);
                Write(importer, r.Nombre); Write(importer, r.Nombre);
                Write(importer, r.Extension); Write(importer, r.Extension);
                Write(importer, r.TamanoOrigen); Write(importer, r.TamanoDestino);
                Write(importer, r.FechaCreacionOrigen?.ToString("O", CultureInfo.InvariantCulture)); Write(importer, r.FechaCreacionDestino?.ToString("O", CultureInfo.InvariantCulture));
                Write(importer, r.FechaModOrigen?.ToString("O", CultureInfo.InvariantCulture)); Write(importer, r.FechaModDestino?.ToString("O", CultureInfo.InvariantCulture));
                Write(importer, r.AtributosOrigen.HasValue ? (long)r.AtributosOrigen.Value : (long?)null); Write(importer, r.AtributosDestino.HasValue ? (long)r.AtributosDestino.Value : (long?)null);
                Write(importer, r.HashOrigen); Write(importer, r.HashDestino); Write(importer, r.Fingerprint); Write(importer, r.Fingerprint);
                Write(importer, r.NombrePcOrigen); Write(importer, r.NombrePcDestino); Write(importer, r.RevisadoOrigen); Write(importer, r.RevisadoDestino);
                importer.Write(r.RepeticionesOrigen); importer.Write(r.RepeticionesDestino);
                Write(importer, r.Candidatura.HasValue ? (long?)r.Candidatura.Value : null); Write(importer, null as long?);
                Write(importer, r.Nombre); Write(importer, r.Extension); Write(importer, r.Fingerprint); importer.Write(r.EsDuplicado);
                Write(importer, Parent(r.RutaOrigen)); Write(importer, Parent(r.RutaDestino));
            }
            importer.Complete();

            using var analyze = _connection.CreateCommand();
            analyze.CommandTimeout = 0;
            analyze.CommandText = "ANALYZE public.comparison_grid_cache;";
            analyze.ExecuteNonQuery();
        }

        private static string Parent(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            int i = p.LastIndexOfAny(new[] { '\\', '/' });
            return i > 0 ? p.Substring(0, i) : string.Empty;
        }

        private static void Write(NpgsqlBinaryImporter i, object value)
        {
            if (value == null) i.WriteNull(); else i.Write(value);
        }

        public List<ComparisonRow> Query(
            long originScanId, long destinationScanId,
            bool showOrigin, bool showDestination,
            IDictionary<string, string> textFilters,
            IDictionary<string, string> fromFilters,
            IDictionary<string, string> toFilters,
            IList<(string col, bool desc)> ordering)
        {
            var result = new List<ComparisonRow>();
            if (!showOrigin && !showDestination) return result;

            var where = new List<string> {
                "c.origin_scan_id=@o", "c.destination_scan_id=@d",
                "((@showO AND c.has_origin) OR (@showD AND c.has_destination))" };
            var parameters = new List<(string, object)> {
                ("@o",originScanId),("@d",destinationScanId),("@showO",showOrigin),("@showD",showDestination)};
            int n = 0;
            foreach (var kv in textFilters)
            {
                var col = Column(kv.Key); if (col == null || string.IsNullOrWhiteSpace(kv.Value)) continue;
                string p = "@t" + (n++); string v = kv.Value.Trim();
                if (v.Length >= 2 && v.StartsWith("*") && v.EndsWith("*")) { where.Add($"COALESCE({col},'') ILIKE '%' || {p} || '%'"); v = v.Substring(1, v.Length - 2); }
                else { where.Add($"COALESCE({col},'') ILIKE {p} || '%'"); }
                parameters.Add((p, v));
            }
            foreach (var kv in fromFilters) AddRange(where, parameters, kv.Key, kv.Value, true, ref n);
            foreach (var kv in toFilters) AddRange(where, parameters, kv.Key, kv.Value, false, ref n);

            string order = BuildOrder(ordering);
            using var cmd = _connection.CreateCommand(); cmd.CommandTimeout = 0;
            cmd.CommandText = $@"SELECT c.row_id,c.origin_id,c.destination_id,c.has_origin,c.has_destination,c.estado,
 c.origin_fullpath,c.destination_fullpath,c.origin_name,c.origin_extension,c.origin_size,c.origin_creationtime,c.origin_lastwritetime,c.origin_attributes,c.origin_hash,c.origin_fingerprint,c.origin_nombrepc,c.origin_revisado,c.origin_repetitions,
 c.destination_fullpath,c.destination_name,c.destination_extension,c.destination_size,c.destination_creationtime,c.destination_lastwritetime,c.destination_attributes,c.destination_hash,c.destination_fingerprint,c.destination_nombrepc,c.destination_revisado,c.destination_repetitions,
 c.nombre,c.extension,c.fingerprint,c.es_duplicado,c.candidatura_origen,c.carpeta_padre_origen,c.carpeta_padre_destino
FROM public.comparison_grid_cache c WHERE {string.Join(" AND ", where)} ORDER BY {order}, c.row_id;";
            foreach (var p in parameters) cmd.Parameters.AddWithValue(p.Item1, p.Item2 ?? DBNull.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read()) result.Add(Read(r));
            return result;
        }

        private static void AddRange(List<string> w, List<(string, object)> ps, string col, string s, bool origin, ref int n)
        {
            if (string.IsNullOrWhiteSpace(s)) return; string c = Column(col); if (c == null) return;
            if (col == "TamanoOrigen" || col == "TamanoDestino")
            {
                if (long.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out var v)) { var p = "@r" + (n++); w.Add($"{c} >= {p}"); ps.Add((p, v)); }
            }
            else if (col == "FechaModOrigen" || col == "FechaModDestino")
            {
                if (DateTime.TryParse(s, out var v)) { var p = "@r" + (n++); w.Add($"NULLIF({c},'')::timestamptz >= {p}"); ps.Add((p, v)); }
            }
        }
        private static string BuildOrder(IList<(string col, bool desc)> o) { if (o == null || o.Count == 0) return "c.row_id ASC"; var a = new List<string>(); foreach (var x in o) { var c = Column(x.col); if (c != null) a.Add(c + (x.desc ? " DESC" : " ASC")); } return a.Count == 0 ? "c.row_id ASC" : string.Join(",", a); }
        private static string Column(string x) => x switch
        {
            "Candidatura" => "c.candidatura_origen",
            "Estado" => "c.estado",
            "CarpetaPadreOrigen" => "c.carpeta_padre_origen",
            "CarpetaPadreDestino" => "c.carpeta_padre_destino",
            "RutaOrigen" => "c.origin_fullpath",
            "RutaDestino" => "c.destination_fullpath",
            "Nombre" => "c.nombre",
            "Extension" => "c.extension",
            "TamanoOrigen" => "c.origin_size",
            "TamanoDestino" => "c.destination_size",
            "FechaModOrigen" => "NULLIF(c.origin_lastwritetime,'')::timestamptz",
            "FechaModDestino" => "NULLIF(c.destination_lastwritetime,'')::timestamptz",
            "HashOrigen" => "c.origin_hash",
            "HashDestino" => "c.destination_hash",
            "Fingerprint" => "c.fingerprint",
            "EsDuplicado" => "CASE WHEN c.es_duplicado THEN 'Sí' ELSE 'No' END",
            "NombrePc" => "COALESCE(c.origin_nombrepc,c.destination_nombrepc)",
            "RevisadoOrigen" => "c.origin_revisado",
            "RevisadoDestino" => "c.destination_revisado",
            _ => null
        };
        private static string S(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetValue(i).ToString();
        private static long? L(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? (long?)null : Convert.ToInt64(r.GetValue(i));
        private static DateTime? D(NpgsqlDataReader r, int i) { var s = S(r, i); return string.IsNullOrEmpty(s) ? (DateTime?)null : DateTime.Parse(s, null, DateTimeStyles.RoundtripKind); }
        private static ComparisonRow Read(NpgsqlDataReader r)
        {
            var x = new ComparisonRow { FileIdOrigen = L(r, 1), FileIdDestino = L(r, 2), Estado = ParseState(S(r, 5)), RutaOrigen = S(r, 6), RutaDestino = S(r, 19), Nombre = S(r, 31), Extension = S(r, 32), TamanoOrigen = L(r, 10), TamanoDestino = L(r, 22), FechaCreacionOrigen = D(r, 11), FechaCreacionDestino = D(r, 23), FechaModOrigen = D(r, 12), FechaModDestino = D(r, 24), HashOrigen = S(r, 14), HashDestino = S(r, 26), Fingerprint = S(r, 33), NombrePcOrigen = S(r, 16), NombrePcDestino = S(r, 28), RevisadoOrigen = S(r, 17), RevisadoDestino = S(r, 29), RepeticionesOrigen = Convert.ToInt32(r.GetValue(18)), RepeticionesDestino = Convert.ToInt32(r.GetValue(30)), Candidatura = L(r, 35).HasValue ? (int?)L(r, 35).Value : null, AtributosOrigen = L(r, 13).HasValue ? (System.IO.FileAttributes?)L(r, 13).Value : null, AtributosDestino = L(r, 25).HasValue ? (System.IO.FileAttributes?)L(r, 25).Value : null, CarpetaPadreOrigen = S(r, 36), CarpetaPadreDestino = S(r, 37) };
            x.Hash = x.HashOrigen ?? x.HashDestino; x.NombrePc = x.NombrePcOrigen ?? x.NombrePcDestino; x.EsDuplicado = r.GetBoolean(35); return x;
        }
        private static FileState ParseState(string s) => Enum.TryParse<FileState>(s, out var v) ? v : FileState.Desconocido;
    }
}
