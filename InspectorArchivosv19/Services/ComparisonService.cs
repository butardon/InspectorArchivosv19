using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using InspectorArchivos.Database;
using Npgsql;

namespace InspectorArchivos.Services
{
    public class ComparisonRow
    {
        public long Id { get; set; }
        public long? OrId { get; set; }
        public string OrName { get; set; }
        public string OrRelativePath { get; set; }
        public long? OrSize { get; set; }
        public string OrHash { get; set; }
        public long? DeId { get; set; }
        public string DeName { get; set; }
        public string DeRelativePath { get; set; }
        public long? DeSize { get; set; }
        public string DeHash { get; set; }
        public string MatchType { get; set; }
    }

    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new List<T>();
        public int TotalCount { get; set; }
    }

    public class ComparisonService
    {
        public async Task<PagedResult<ComparisonRow>> GetPagedRowsAsync(
            int pageIndex,
            int pageSize,
            string filterText,
            string sortColumn,
            bool ascending)
        {
            using var conn = DbFactory.CreateConnection();
            await conn.OpenAsync();

            var whereClauses = new List<string>();
            using var cmd = conn.CreateCommand();

            if (!string.IsNullOrWhiteSpace(filterText))
            {
                whereClauses.Add("(or_name ILIKE @filter OR de_name ILIKE @filter OR or_relative_path ILIKE @filter OR de_relative_path ILIKE @filter)");
                cmd.Parameters.AddWithValue("filter", $"%{filterText}%");
            }

            string whereSql = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

            string safeSortColumn = sortColumn switch
            {
                "OrName" => "or_name",
                "OrRelativePath" => "or_relative_path",
                "OrSize" => "or_size",
                "DeName" => "de_name",
                "DeRelativePath" => "de_relative_path",
                "DeSize" => "de_size",
                "MatchType" => "match_type",
                _ => "id"
            };
            string dirSql = ascending ? "ASC" : "DESC";

            cmd.CommandText = $@"
                SELECT id, or_id, or_name, or_relative_path, or_size, or_hash, 
                       de_id, de_name, de_relative_path, de_size, de_hash, match_type,
                       COUNT(*) OVER() as total_rows
                FROM comparison_grid_cache
                {whereSql}
                ORDER BY {safeSortColumn} {dirSql}
                OFFSET @offset LIMIT @limit;";

            cmd.Parameters.AddWithValue("offset", pageIndex * pageSize);
            cmd.Parameters.AddWithValue("limit", pageSize);

            var result = new PagedResult<ComparisonRow>();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                if (result.TotalCount == 0)
                    result.TotalCount = Convert.ToInt32(reader["total_rows"]);

                result.Items.Add(new ComparisonRow
                {
                    Id = reader.GetInt64(0),
                    OrId = reader.IsDBNull(1) ? null : reader.GetInt64(1),
                    OrName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    OrRelativePath = reader.IsDBNull(3) ? null : reader.GetString(3),
                    OrSize = reader.IsDBNull(4) ? null : reader.GetInt64(4),
                    OrHash = reader.IsDBNull(5) ? null : reader.GetString(5),
                    DeId = reader.IsDBNull(6) ? null : reader.GetInt64(6),
                    DeName = reader.IsDBNull(7) ? null : reader.GetString(7),
                    DeRelativePath = reader.IsDBNull(8) ? null : reader.GetString(8),
                    DeSize = reader.IsDBNull(9) ? null : reader.GetInt64(9),
                    DeHash = reader.IsDBNull(10) ? null : reader.GetString(10),
                    MatchType = reader.IsDBNull(11) ? null : reader.GetString(11)
                });
            }

            return result;
        }

        public async Task RebuildComparisonCacheAsync(long scanSourceId, long scanTargetId)
        {
            using var conn = DbFactory.CreateConnection();
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 600;
            cmd.CommandText = @"
                TRUNCATE TABLE comparison_grid_cache;

                INSERT INTO comparison_grid_cache (or_id, or_name, or_relative_path, or_size, or_hash, de_id, de_name, de_relative_path, de_size, de_hash, match_type)
                SELECT 
                    s.id, s.name, s.relative_path, s.size, s.hash_blake3,
                    t.id, t.name, t.relative_path, t.size, t.hash_blake3,
                    CASE 
                        WHEN s.hash_blake3 = t.hash_blake3 THEN 'IDENTICAL'
                        WHEN s.name = t.name AND s.size = t.size THEN 'SAME_NAME_SIZE'
                        WHEN s.id IS NULL THEN 'ONLY_TARGET'
                        ELSE 'ONLY_SOURCE'
                    END
                FROM files s
                FULL OUTER JOIN files t ON s.hash_blake3 = t.hash_blake3 AND t.scan_id = @targetId
                WHERE s.scan_id = @sourceId OR t.scan_id = @targetId;";

            cmd.Parameters.AddWithValue("sourceId", scanSourceId);
            cmd.Parameters.AddWithValue("targetId", scanTargetId);

            await cmd.ExecuteNonQueryAsync();
        }
    }
}