using System.Threading.Tasks;
using Npgsql;

namespace InspectorArchivos.Database
{
    public static class DatabaseSchemaInitializer
    {
        public static async Task EnsureOptimizedSchemaAsync()
        {
            using var conn = DbFactory.CreateConnection();
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE EXTENSION IF NOT EXISTS pg_trgm;

                CREATE TABLE IF NOT EXISTS comparison_grid_cache (
                    id BIGSERIAL PRIMARY KEY,
                    or_id BIGINT,
                    or_name TEXT,
                    or_relative_path TEXT,
                    or_size BIGINT,
                    or_hash TEXT,
                    de_id BIGINT,
                    de_name TEXT,
                    de_relative_path TEXT,
                    de_size BIGINT,
                    de_hash TEXT,
                    match_type VARCHAR(50)
                );

                CREATE INDEX IF NOT EXISTS idx_cgc_or_name_trgm ON comparison_grid_cache USING gin (or_name gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS idx_cgc_de_name_trgm ON comparison_grid_cache USING gin (de_name gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS idx_cgc_or_rel_trgm ON comparison_grid_cache USING gin (or_relative_path gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS idx_cgc_de_rel_trgm ON comparison_grid_cache USING gin (de_relative_path gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS idx_cgc_match_type ON comparison_grid_cache (match_type);
                CREATE INDEX IF NOT EXISTS idx_cgc_or_size ON comparison_grid_cache (or_size);
                CREATE INDEX IF NOT EXISTS idx_cgc_de_size ON comparison_grid_cache (de_size);
            ";
            await cmd.ExecuteNonQueryAsync();
        }
    }
}