using System;
using System.IO;

namespace InspectorArchivos.Models
{
    /// <summary>
    /// Representa un archivo individual indexado durante un escaneo.
    /// Cada registro tiene un GUID permanente que permite comparar escaneos
    /// y un Fingerprint (Tamaño + Fecha de modificación + Hash) para detectar
    /// renombrados, movidos, duplicados y modificaciones.
    /// </summary>
    public class FileRecord
    {
        public long Id { get; set; }
        public Guid Guid { get; set; }
        public long ScanId { get; set; }
        public string FullPath { get; set; }
        public string RootPath { get; set; }
        public string Name { get; set; }
        public string Extension { get; set; }
        public long Size { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime LastWriteTime { get; set; }
        public DateTime LastAccessTime { get; set; }
        public FileAttributes Attributes { get; set; }
        public string Hash { get; set; }
        public string Fingerprint { get; set; }
        public DateTime ScannedAt { get; set; }

        /// <summary>Procedencia del archivo: 'origen' o 'destino'.</summary>
        public string Procedencia { get; set; }

        /// <summary>Nombre del equipo (PC) donde se realizó el escaneo de este archivo.</summary>
        public string NombrePc { get; set; }

        /// <summary>
        /// Candidatura dentro de un grupo de duplicados (mismo hash): 1 = archivo a
        /// conservar; &gt;1 = copia duplicada. NULL si el archivo no forma parte de
        /// ningún grupo de duplicados o aún no se ha revisado.
        /// </summary>
        public int? Candidatura { get; set; }

        /// <summary>
        /// Valor ('S', 'N' o vacío/null) tomado de Historial.Revisado al escanear este
        /// archivo como parte de un escaneo de ORIGEN. Se rellena a 'N' si no se
        /// encontró el archivo en Historial o su Revisado estaba vacío. Solo se
        /// establece cuando el escaneo es de ámbito Origen; en un escaneo de Destino
        /// queda a null.
        /// </summary>
        public string RevisadoOrigen { get; set; }

        /// <summary>
        /// Igual que <see cref="RevisadoOrigen"/> pero para escaneos de ámbito Destino.
        /// </summary>
        public string RevisadoDestino { get; set; }

        /// <summary>Directorio padre del archivo (carpeta contenedora).</summary>
        public string ParentFolder => string.IsNullOrEmpty(FullPath)
            ? string.Empty
            : Path.GetDirectoryName(FullPath) ?? string.Empty;

        /// <summary>
        /// Nuevo campo: tipo de archivo. Solo permite null, "imagen" o "no-imagen".
        /// </summary>
        public string TipoArchivo { get; set; }

        /// <summary>
        /// Calcula el fingerprint del archivo: Tamaño + Fecha de modificación (UTC ticks) + Hash.
        /// </summary>
        public static string ComputeFingerprint(long size, DateTime lastWriteTimeUtc, string hash)
        {
            return $"{size}|{lastWriteTimeUtc.ToFileTimeUtc()}|{hash ?? string.Empty}";
        }

        public string FingerprintFromFields() =>
            ComputeFingerprint(Size, LastWriteTime, Hash);
    }
}


