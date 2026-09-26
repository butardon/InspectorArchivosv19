CREATE TABLE IF NOT EXISTS public.comparison_grid_cache (
 origin_scan_id bigint NOT NULL, destination_scan_id bigint NOT NULL, row_id bigint NOT NULL,
 origin_id bigint NULL, destination_id bigint NULL, has_origin boolean NOT NULL, has_destination boolean NOT NULL,
 estado text NOT NULL, origin_fullpath text NULL, destination_fullpath text NULL,
 origin_name text NULL, destination_name text NULL, origin_extension text NULL, destination_extension text NULL,
 origin_size bigint NULL, destination_size bigint NULL, origin_creationtime text NULL, destination_creationtime text NULL,
 origin_lastwritetime text NULL, destination_lastwritetime text NULL, origin_attributes bigint NULL, destination_attributes bigint NULL,
 origin_hash text NULL, destination_hash text NULL, origin_fingerprint text NULL, destination_fingerprint text NULL,
 origin_nombrepc text NULL, destination_nombrepc text NULL, origin_revisado text NULL, destination_revisado text NULL,
 origin_repetitions integer NOT NULL, destination_repetitions integer NOT NULL,
 origin_candidatura bigint NULL, destination_candidatura bigint NULL,
 nombre text NULL, extension text NULL, fingerprint text NULL, es_duplicado boolean NOT NULL,
 carpeta_padre_origen text NULL, carpeta_padre_destino text NULL,
 PRIMARY KEY (origin_scan_id, destination_scan_id, row_id)
);
CREATE INDEX IF NOT EXISTS ix_cgc_presence ON public.comparison_grid_cache (origin_scan_id,destination_scan_id,has_origin,has_destination,row_id);
CREATE INDEX IF NOT EXISTS ix_cgc_estado ON public.comparison_grid_cache (origin_scan_id,destination_scan_id,estado,row_id);
CREATE INDEX IF NOT EXISTS ix_cgc_or_name ON public.comparison_grid_cache (origin_scan_id,destination_scan_id,origin_name,row_id);
CREATE INDEX IF NOT EXISTS ix_cgc_de_name ON public.comparison_grid_cache (origin_scan_id,destination_scan_id,destination_name,row_id);
CREATE INDEX IF NOT EXISTS ix_cgc_or_path ON public.comparison_grid_cache (origin_scan_id,destination_scan_id,origin_fullpath,row_id);
CREATE INDEX IF NOT EXISTS ix_cgc_de_path ON public.comparison_grid_cache (origin_scan_id,destination_scan_id,destination_fullpath,row_id);
CREATE INDEX IF NOT EXISTS ix_cgc_or_size ON public.comparison_grid_cache (origin_scan_id,destination_scan_id,origin_size,row_id);
CREATE INDEX IF NOT EXISTS ix_cgc_de_size ON public.comparison_grid_cache (origin_scan_id,destination_scan_id,destination_size,row_id);
CREATE INDEX IF NOT EXISTS ix_cgc_or_date ON public.comparison_grid_cache (origin_scan_id,destination_scan_id,origin_lastwritetime,row_id);
CREATE INDEX IF NOT EXISTS ix_cgc_de_date ON public.comparison_grid_cache (origin_scan_id,destination_scan_id,destination_lastwritetime,row_id);
ANALYZE public.comparison_grid_cache;
