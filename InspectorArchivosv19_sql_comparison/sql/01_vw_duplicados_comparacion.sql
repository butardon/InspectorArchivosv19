-- Vista de apoyo para la comparativa origen/destino.
-- Mantiene la vista vw_duplicados existente intacta para no romper otras partes
-- de la aplicación. Esta vista devuelve solamente la clave de contenido que está
-- duplicada y su número de apariciones dentro del mismo scan/procedencia.
--
-- Definición de duplicado:
--   mismo scanid + misma procedencia + mismo hash + mismo size
--
-- La vista NO incluye los millones de filas de files; solo una fila por clave
-- duplicada, por lo que es mucho más adecuada para ser utilizada desde la
-- comparativa SQL.

CREATE OR REPLACE VIEW public.vw_duplicados_comparacion AS
SELECT
    f.scanid,
    f.procedencia,
    f.hash,
    f.size,
    COUNT(*)::int AS dup_count
FROM public.files AS f
WHERE f.hash IS NOT NULL
  AND f.hash <> ''
GROUP BY
    f.scanid,
    f.procedencia,
    f.hash,
    f.size
HAVING COUNT(*) > 1;

-- Índice recomendado para el acceso de la comparativa y de la vista.
-- El índice existente (scanid, hash) sigue siendo válido, pero este añade size
-- y evita más trabajo al localizar las claves de contenido.
CREATE INDEX IF NOT EXISTS ix_files_scanid_procedencia_hash_size
    ON public.files (scanid, procedencia, hash, size);

-- Índice para reducir el coste de recuperar los archivos de cada escaneo.
CREATE INDEX IF NOT EXISTS ix_files_scanid_fullpath
    ON public.files (scanid, fullpath);

-- Después de crear índices/cargar muchos datos:
-- ANALYZE public.files;
-- ANALYZE public.scans;
