using InspectorArchivos.Models;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace InspectorArchivos.Data
{
    public class ComparisonRepository
    {
        private readonly string _connectionString;

        public ComparisonRepository(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public async Task<bool> IsCacheValidAsync(long originScanId, long destinationScanId, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT EXISTS (
                    SELECT 1 
                    FROM comparison_grid_cache 
                    WHERE origin_scan_id = @originScanId 
                      AND destination_scan_id = @destinationScanId
                    LIMIT 1
                );";

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct);
                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@originScanId", originScanId);
                    cmd.Parameters.AddWithValue("@destinationScanId", destinationScanId);
                    var result = await cmd.ExecuteScalarAsync(ct);
                    return result is bool exists && exists;
                }
            }
        }

        public async Task<long> PopulateCacheInDatabaseAsync(long originScanId, long destinationScanId, CancellationToken ct = default)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct);
                using (var tx = await conn.BeginTransactionAsync(ct))
                {
                    const string deleteSql = @"
                        DELETE FROM comparison_grid_cache 
                        WHERE origin_scan_id = @originScanId 
                          AND destination_scan_id = @destinationScanId;";

                    using (var deleteCmd = new NpgsqlCommand(deleteSql, conn, tx))
                    {
                        deleteCmd.Parameters.AddWithValue("@originScanId", originScanId);
                        deleteCmd.Parameters.AddWithValue("@destinationScanId", destinationScanId);
                        await deleteCmd.ExecuteNonQueryAsync(ct);
                    }

                    // Inserta usando FULL OUTER JOIN sobre scan_files.
                    // Los valores de estado usan los mismos nombres que el enum FileState
                    // (Igual, Diferente, SoloOrigen, SoloDestino, etc.).
                    const string insertSql = @"
                        INSERT INTO comparison_grid_cache (
                            origin_scan_id, destination_scan_id, row_id,
                            rel_path, filename, status_code, origin_size, dest_size,
                            origin_mtime, dest_mtime, origin_hash, dest_hash
                        )
                        SELECT 
                            @originScanId,
                            @destinationScanId,
                            ROW_NUMBER() OVER (ORDER BY COALESCE(o.rel_path, d.rel_path)) AS row_id,
                            COALESCE(o.rel_path, d.rel_path) AS rel_path,
                            COALESCE(o.filename, d.filename) AS filename,
                            CASE 
                                WHEN o.file_id IS NOT NULL AND d.file_id IS NOT NULL AND o.blake3_hash = d.blake3_hash THEN 'Igual'
                                WHEN o.file_id IS NOT NULL AND d.file_id IS NOT NULL AND o.blake3_hash <> d.blake3_hash THEN 'MismoHashDistintoNombre'
                                WHEN o.file_id IS NOT NULL AND d.file_id IS NULL THEN 'SoloOrigen'
                                ELSE 'SoloDestino'
                            END AS status_code,
                            o.file_size AS origin_size,
                            d.file_size AS dest_size,
                            o.mtime AS origin_mtime,
                            d.mtime AS dest_mtime,
                            o.blake3_hash AS origin_hash,
                            d.blake3_hash AS dest_hash
                        FROM (SELECT * FROM scan_files WHERE scan_id = @originScanId) o
                        FULL OUTER JOIN (SELECT * FROM scan_files WHERE scan_id = @destinationScanId) d
                        ON o.rel_path = d.rel_path;";

                    long rowsInserted = 0;
                    using (var insertCmd = new NpgsqlCommand(insertSql, conn, tx))
                    {
                        insertCmd.CommandTimeout = 300;
                        insertCmd.Parameters.AddWithValue("@originScanId", originScanId);
                        insertCmd.Parameters.AddWithValue("@destinationScanId", destinationScanId);
                        rowsInserted = await insertCmd.ExecuteNonQueryAsync(ct);
                    }

                    await tx.CommitAsync(ct);

                    using (var analyzeCmd = new NpgsqlCommand("ANALYZE comparison_grid_cache;", conn))
                    {
                        await analyzeCmd.ExecuteNonQueryAsync(ct);
                    }

                    return rowsInserted;
                }
            }
        }

        public async Task<int> GetTotalCountAsync(
            long originScanId,
            long destinationScanId,
            string filterText = null,
            string statusFilter = null,
            CancellationToken ct = default)
        {
            var sb = new StringBuilder(@"
                SELECT COUNT(*) 
                FROM comparison_grid_cache 
                WHERE origin_scan_id = @originScanId 
                  AND destination_scan_id = @destinationScanId");

            var parameters = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@originScanId", originScanId),
                new NpgsqlParameter("@destinationScanId", destinationScanId)
            };

            if (!string.IsNullOrWhiteSpace(statusFilter) && !statusFilter.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(" AND status_code = @statusFilter");
                parameters.Add(new NpgsqlParameter("@statusFilter", statusFilter));
            }

            if (!string.IsNullOrWhiteSpace(filterText))
            {
                sb.Append(" AND (rel_path ILIKE @filterText OR filename ILIKE @filterText)");
                parameters.Add(new NpgsqlParameter("@filterText", "%" + filterText + "%"));
            }

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct);
                using (var cmd = new NpgsqlCommand(sb.ToString(), conn))
                {
                    cmd.Parameters.AddRange(parameters.ToArray());
                    var result = await cmd.ExecuteScalarAsync(ct);
                    return Convert.ToInt32(result);
                }
            }
        }

        public async Task<List<ComparisonRow>> GetPagedRowsAsync(
            long originScanId,
            long destinationScanId,
            int offset,
            int limit,
            string sortColumn = "rel_path",
            bool sortAscending = true,
            string filterText = null,
            string statusFilter = null,
            CancellationToken ct = default)
        {
            var rows = new List<ComparisonRow>();
            var sb = new StringBuilder(@"
                SELECT 
                    row_id, rel_path, filename, status_code, 
                    origin_size, dest_size, origin_mtime, dest_mtime, 
                    origin_hash, dest_hash
                FROM comparison_grid_cache
                WHERE origin_scan_id = @originScanId 
                  AND destination_scan_id = @destinationScanId");

            var parameters = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@originScanId", originScanId),
                new NpgsqlParameter("@destinationScanId", destinationScanId)
            };

            if (!string.IsNullOrWhiteSpace(statusFilter) && !statusFilter.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(" AND status_code = @statusFilter");
                parameters.Add(new NpgsqlParameter("@statusFilter", statusFilter));
            }

            if (!string.IsNullOrWhiteSpace(filterText))
            {
                sb.Append(" AND (rel_path ILIKE @filterText OR filename ILIKE @filterText)");
                parameters.Add(new NpgsqlParameter("@filterText", "%" + filterText + "%"));
            }

            string safeSortColumn = sortColumn?.ToLowerInvariant() switch
            {
                "filename" => "filename",
                "statuscode" => "status_code",
                "status_code" => "status_code",
                "originsize" => "origin_size",
                "origin_size" => "origin_size",
                "destsize" => "dest_size",
                "dest_size" => "dest_size",
                "originmtime" => "origin_mtime",
                "origin_mtime" => "origin_mtime",
                "destmtime" => "dest_mtime",
                "dest_mtime" => "dest_mtime",
                _ => "rel_path"
            };

            string direction = sortAscending ? "ASC" : "DESC";
            sb.Append($" ORDER BY {safeSortColumn} {direction} OFFSET @offset LIMIT @limit");

            parameters.Add(new NpgsqlParameter("@offset", offset));
            parameters.Add(new NpgsqlParameter("@limit", limit));

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct);
                using (var cmd = new NpgsqlCommand(sb.ToString(), conn))
                {
                    cmd.Parameters.AddRange(parameters.ToArray());
                    using (var reader = await cmd.ExecuteReaderAsync(ct))
                    {
                        while (await reader.ReadAsync(ct))
                        {
                            string rawStatus = reader.GetString(3);
                            Enum.TryParse(rawStatus, true, out FileState parsedState);

                            rows.Add(new ComparisonRow
                            {
                                RowId = reader.GetInt64(0),
                                RelPath = reader.IsDBNull(1) ? null : reader.GetString(1),
                                Filename = reader.IsDBNull(2) ? null : reader.GetString(2),
                                Estado = parsedState,
                                TamanoOrigen = reader.IsDBNull(4) ? (long?)null : reader.GetInt64(4),
                                TamanoDestino = reader.IsDBNull(5) ? (long?)null : reader.GetInt64(5),
                                FechaModOrigen = reader.IsDBNull(6) ? (DateTime?)null : reader.GetDateTime(6),
                                FechaModDestino = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7),
                                HashOrigen = reader.IsDBNull(8) ? null : reader.GetString(8),
                                HashDestino = reader.IsDBNull(9) ? null : reader.GetString(9)
                            });
                        }
                    }
                }
            }

            return rows;
        }
    }
}
