using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;


namespace InspectorArchivos.Utils
{
    /// <summary>
    /// Utilidades para determinar la fecha de modificación "válida" de un archivo
    /// combinando la fecha detectada en su NOMBRE con la fecha EXIF/modificación.
    /// </summary>
    public static class DateUtils
    {
        private static readonly DateTime MinValid = new DateTime(1900, 1, 1);
        private static readonly DateTime MaxValid = new DateTime(2100, 12, 31, 23, 59, 59);

        // Formatos españoles con años de 2 o 4 dígitos y separadores / - .
        private static readonly string[] Formats =
        {   "yyyyMMdd", "ddMMyyyy", "ddMMyy",
            "yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd",
            "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy",
            "d.M.yyyy", "dd.MM.yyyy",
            "d/M/yy", "dd/MM/yy", "d-M-yy", "dd-MM-yy", "d.M.yy", "dd.MM.yy"            
        };

        // Tokens candidatos dentro del nombre: fechas con separadores y bloques compactos.
        private static readonly Regex[] Candidates =
        {
            new Regex(@"\d{4}[/\-.]\d{1,2}[/\-.]\d{1,2}", RegexOptions.Compiled),
            new Regex(@"\d{8}", RegexOptions.Compiled),
            new Regex(@"\d{1,2}[/\-.]\d{1,2}[/\-.]\d{2,4}", RegexOptions.Compiled),
            new Regex(@"\d{6}", RegexOptions.Compiled)
        };

        /// <summary>
        /// Intenta extraer una fecha válida (entre 01/01/1900 y 31/12/2100) del nombre
        /// del archivo, probando todos los formatos españoles razonables. Si se detectan
        /// varias, devuelve la más temprana. Devuelve null si no hay ninguna válida.
        /// </summary>
        //public static DateTime? TryParseDateFromFileName(string fileName)
        //{
        //    if (string.IsNullOrWhiteSpace(fileName)) return null;

        //    // Se trabaja sobre el nombre sin extensión para no confundir la extensión con dígitos.
        //    string name = System.IO.Path.GetFileNameWithoutExtension(fileName);
        //    DateTime? best = null;

        //    foreach (var rx in Candidates)
        //    {
        //        foreach (Match match in rx.Matches(name))
        //        {
        //            string token = match.Value;
        //            foreach (var fmt in Formats)
        //            {
        //                // TryParseExact exige coincidencia total del token con el formato,
        //                // por lo que descarta por sí solo las longitudes incompatibles.
        //                if (DateTime.TryParseExact(token, fmt, CultureInfo.GetCultureInfo("es-ES"),
        //                        DateTimeStyles.None, out var dt)
        //                    && dt >= MinValid && dt <= MaxValid)
        //                {
        //                    if (best == null || dt < best.Value) best = dt;
        //                }
        //            }
        //        }
        //    }
        //    return best;
        //}

        public static DateTime? TryParseDateFromFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;

            // Se trabaja sobre el nombre sin extensión para no confundir la extensión con dígitos.
            string name = System.IO.Path.GetFileNameWithoutExtension(fileName);

            // Guardamos, para cada coincidencia válida, su posición de inicio en el
            // nombre (match.Index) y la fecha resultante. Así podemos elegir la que
            // aparece más a la izquierda, en vez de la cronológicamente más antigua.
            int bestIndex = int.MaxValue;
            DateTime? best = null;

            foreach (var rx in Candidates)
            {
                foreach (Match match in rx.Matches(name))
                {
                    // Si esta coincidencia empieza más a la derecha que la mejor
                    // encontrada hasta ahora, no puede mejorarla: la descartamos
                    // sin ni siquiera intentar parsearla.
                    if (match.Index > bestIndex) continue;

                    string token = match.Value;
                    foreach (var fmt in Formats)
                    {
                        // TryParseExact exige coincidencia total del token con el formato,
                        // por lo que descarta por sí solo las longitudes incompatibles.
                        if (DateTime.TryParseExact(token, fmt, CultureInfo.GetCultureInfo("es-ES"),
                                DateTimeStyles.None, out var dt)
                            && dt >= MinValid && dt <= MaxValid)
                        {
                            // Criterio de selección: prioridad a la posición más a la
                            // izquierda (match.Index). En caso de empate de posición
                            // (p. ej. el mismo token interpretado con dos formatos
                            // distintos, o dos regex distintos casando en el mismo
                            // punto), se mantiene el criterio anterior de quedarnos
                            // con la fecha más temprana como desempate.
                            if (match.Index < bestIndex ||
                                (match.Index == bestIndex && (best == null || dt < best.Value)))
                            {
                                bestIndex = match.Index;
                                best = dt;
                            }
                        }
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// Intenta leer la fecha EXIF en que se tomó la foto (tag 0x9003 "DateTimeOriginal",
        /// o 0x0132 "DateTime" como respaldo). Devuelve null si el archivo no es imagen,
        /// no tiene el tag informado, o no se puede parsear.
        /// </summary>
        public static DateTime? TryGetExifDateTaken(string filePath)
        {
            if ( !File.Exists(filePath) )  return null;

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var img = Image.FromStream(fs, useEmbeddedColorManagement: false, validateImageData: false);

                // 0x9003 = DateTimeOriginal, 0x0132 = DateTime (fallback)
                int[] tagsToTry = { 0x9003, 0x9004, 0x0132 };

                foreach (var tagId in tagsToTry)
                {
                    if (Array.IndexOf(img.PropertyIdList, tagId) < 0) continue;

                    var prop = img.GetPropertyItem(tagId);
                    // El valor EXIF viene como ASCII con formato "yyyy:MM:dd HH:mm:ss\0"
                    var raw = System.Text.Encoding.ASCII.GetString(prop.Value).TrimEnd('\0');

                    if (DateTime.TryParseExact(
                            raw, "yyyy:MM:dd HH:mm:ss",
                            CultureInfo.InvariantCulture, DateTimeStyles.None,
                            out var parsed))
                    {
                        return parsed;
                    }
                }
            }
            catch
            {
                // Archivo corrupto, formato no soportado, sin metadatos, etc.
                // Se ignora y se devuelve null.
            }

            return null;
        }

        /// <summary>
        /// Calcula la fecha de modificación válida de un archivo:
        /// - Si el nombre contiene una fecha válida, se compara con la fecha EXIF/modificación
        ///   (o se usa esta última si son iguales), y con la fecha de creación, quedándonos
        ///   siempre con la MENOR de todas.
        /// - Si además el archivo es una imagen y el check "solo imágenes" está activo, también
        ///   se compara con la fecha EXIF de captura (exifDateTaken), que solo suele venir
        ///   informada en imágenes (los demás tipos de archivo normalmente no la tienen).
        /// - Si el nombre no contiene fecha válida, se devuelve la fecha EXIF/modificación,
        ///   salvo que la fecha EXIF de captura sea aún menor y aplique la comparación.
        /// </summary>
        /// <param name="fileName">Nombre del archivo.</param>
        /// <param name="exifModified">Fecha EXIF de modificación (o fecha de modificación del archivo).</param>
        /// <param name="creationDate">Fecha de creación del archivo.</param>
        /// <param name="exifDateTaken">Fecha EXIF en que se tomó la imagen (puede ser null si no aplica o no está informada).</param>
        /// <param name="onlyImagesExifCheck">Indica si está marcado el check de "solo imágenes": solo si es true se tendrá en cuenta exifDateTaken.</param>
        public static DateTime GetValidModificationDate(
            string fileName,
            DateTime exifModified,
            DateTime creationDate,
            DateTime? exifDateTaken = null,
            bool onlyImagesExifCheck = false)
        {
            var fromName = TryParseDateFromFileName(fileName);

            // Punto de partida: si hay fecha en el nombre, la lógica original
            // (mínimo entre nombre y exifModified); si no, exifModified directamente.
            DateTime earliest = fromName == null
                ? exifModified
                : (fromName.Value < exifModified ? fromName.Value : exifModified);

            // Comparamos también con la fecha de creación: si es más antigua,
            // se convierte en la fecha final provisional.
            if (creationDate < earliest) earliest = creationDate;

            // Si el check de "solo imágenes" está activo y tenemos fecha EXIF de
            // captura informada, la comparamos también: si es menor que todas las
            // demás, será la que finalmente se devuelva.
            if (onlyImagesExifCheck && exifDateTaken.HasValue && exifDateTaken.Value < earliest)
            {
                earliest = exifDateTaken.Value;
            }

            return earliest;
        }

        /// <summary>
        /// Tolerancia usada para considerar dos fechas de archivo "iguales" al
        /// comparar contra lo guardado en el escaneo (ver
        /// <see cref="AreFileTimesEquivalent"/>). Es necesaria porque:
        /// - Algunos sistemas de archivos (p. ej. FAT32) solo guardan la fecha de
        ///   modificación con una resolución de 2 segundos, y la de creación con
        ///   una resolución de 10 ms, mientras que NTFS usa 100 ns.
        /// - Copias por red (SMB) o ciertas herramientas pueden truncar o
        ///   redondear los milisegundos/ticks al transferir el archivo.
        /// Sin esta tolerancia, dos fechas que se ven idénticas en pantalla
        /// (hasta el segundo) pueden compararse como distintas por diferencias
        /// de sub-segundo que no se muestran al usuario.
        /// </summary>
        private static readonly TimeSpan FileTimeTolerance = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Compara dos fechas de archivo (creación o modificación) de forma robusta
        /// frente a: distinto <see cref="DateTimeKind"/> (Local/Utc/Unspecified) entre
        /// el valor recién leído del disco y el guardado en el escaneo, y pequeñas
        /// diferencias de sub-segundo debidas a la resolución del sistema de archivos
        /// o a copias/transferencias. Ambas fechas se normalizan a UTC antes de
        /// comparar, y se consideran iguales si su diferencia absoluta no supera
        /// <see cref="FileTimeTolerance"/>.
        /// </summary>
        public static bool AreFileTimesEquivalent(DateTime a, DateTime b)
        {
            DateTime aUtc = ToComparableUtc(a);
            DateTime bUtc = ToComparableUtc(b);
            TimeSpan diff = aUtc > bUtc ? aUtc - bUtc : bUtc - aUtc;
            return diff <= FileTimeTolerance;
        }

        /// <summary>
        /// Normaliza una fecha a UTC para comparación, tratando
        /// <see cref="DateTimeKind.Unspecified"/> como hora local (que es lo que
        /// devuelven <c>FileInfo.CreationTime</c>/<c>LastWriteTime</c> y lo que se
        /// reconstruye al leer el escaneo desde la base de datos).
        /// </summary>
        private static DateTime ToComparableUtc(DateTime d)
        {
            return d.Kind switch
            {
                DateTimeKind.Utc => d,
                DateTimeKind.Local => d.ToUniversalTime(),
                _ => DateTime.SpecifyKind(d, DateTimeKind.Local).ToUniversalTime()
            };
        }
    }
}

