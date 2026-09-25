using System;
using System.IO;

namespace InspectorArchivos.Models
{
    /// <summary>
    /// Estados posibles de un archivo al comparar origen y destino.
    /// </summary>
    public enum FileState
    {
        /// <summary>Existe solo en origen.</summary>
        SoloOrigen,
        /// <summary>Existe solo en destino.</summary>
        SoloDestino,
        /// <summary>Existe en ambos con contenido idéntico (mismo hash) y mismo nombre.</summary>
        Igual,
        /// <summary>Existe en ambos con mismo hash pero distinto nombre (contenido duplicado).</summary>
        MismoHashDistintoNombre,
        /// <summary>Mismo nombre/ruta relativa pero hash distinto (modificado).</summary>
     //   Modificado,
        /// <summary>Hash y fingerprint coinciden pero la ruta es distinta (movido o renombrado).</summary>
     //   MovidoORenombrado,
        /// <summary>Varios archivos con el mismo hash dentro de origen (duplicados en origen).</summary>
        DuplicadoOrigen,
        /// <summary>Varios archivos con el mismo hash dentro de destino (duplicados en destino).</summary>
        DuplicadoDestino,
        /// <summary>Duplicados tanto en origen como en destino.</summary>
        DuplicadoAmbos,
        /// <summary>No se pudo determinar (sin hash, por ejemplo).</summary>
        Desconocido
    }

    /// <summary>
    /// Fila de la tabla comparativa origen/destino.
    /// Se construye agrupando archivos por hash entre el escaneo de origen y el de destino.
    /// </summary>
    public class ComparisonRow
    {
        public bool Selected { get; set; }
        public FileState Estado { get; set; }
        public string EstadoLabel => EstadoToString(Estado);

        public string CarpetaPadreOrigen { get; set; }
        public string CarpetaPadreDestino { get; set; }
        public string RutaOrigen { get; set; }
        public string RutaDestino { get; set; }
        public string Nombre { get; set; }
        public string Extension { get; set; }

        public long? TamanoOrigen { get; set; }
        public long? TamanoDestino { get; set; }
        public DateTime? FechaModOrigen { get; set; }
        public DateTime? FechaModDestino { get; set; }

        public string Hash { get; set; }
        public string HashOrigen { get; set; }
        public string HashDestino { get; set; }
        public string Fingerprint { get; set; }
        public bool EsDuplicado { get; set; }
        public int RepeticionesOrigen { get; set; }
        public int RepeticionesDestino { get; set; }

        // ---- Nombre de equipo (NombrePc) y campos usados para el filtrado por
        // arrastrar y soltar desde el explorador (comparación estricta contra el
        // escaneo en curso: NombrePc + ruta completa + nombre y extensión +
        // fecha de modificación + fecha de creación + atributos). ----
        /// <summary>Nombre del equipo combinado (para la columna de la cuadrícula): el de origen si existe, si no el de destino.</summary>
        public string NombrePc { get; set; }
        public string NombrePcOrigen { get; set; }
        public string NombrePcDestino { get; set; }
        public DateTime? FechaCreacionOrigen { get; set; }
        public DateTime? FechaCreacionDestino { get; set; }
        public FileAttributes? AtributosOrigen { get; set; }
        public FileAttributes? AtributosDestino { get; set; }

        // ---- Revisado (Historial) por lado, mostrado y filtrable en la cuadrícula
        // comparativa igual que el resto de columnas. Valores: 'S', 'N' o vacío. ----
        /// <summary>Valor de Historial.Revisado para el archivo de origen (columna Files.RevisadoOrigen).</summary>
        public string RevisadoOrigen { get; set; }
        /// <summary>Valor de Historial.Revisado para el archivo de destino (columna Files.RevisadoDestino).</summary>
        public string RevisadoDestino { get; set; }

        // ---- Revisar Duplicados ----
        /// <summary>Candidatura dentro del grupo de duplicados: 1 = conservar, &gt;1 = duplicado. NULL si no aplica.</summary>
        public int? Candidatura { get; set; }
        /// <summary>Id del archivo de origen (cuando la fila representa un archivo de origen).</summary>
        public long? FileIdOrigen { get; set; }
        /// <summary>Id del archivo de destino (cuando la fila representa un archivo de destino).</summary>
        public long? FileIdDestino { get; set; }
        /// <summary>Lado del archivo que representa la fila en modo revisión: "origen" o "destino".</summary>
        public string ProcedenciaArchivo { get; set; }

        public static string EstadoToString(FileState s) => s switch
        {
            FileState.SoloOrigen => "Solo en origen",
            FileState.SoloDestino => "Solo en destino",
            FileState.Igual => "Igual (idéntico)",
            FileState.MismoHashDistintoNombre => "Mismo hash, distinto nombre",
            //FileState.Modificado => "Modificado",
            //FileState.MovidoORenombrado => "Movido/Renombrado",
            FileState.DuplicadoOrigen => "Duplicado en origen",
            FileState.DuplicadoDestino => "Duplicado en destino",
            FileState.DuplicadoAmbos => "Duplicado en ambos",
            FileState.Desconocido => "Desconocido",
            _ => s.ToString()
        };

        /// <summary>Indica si la fila representa contenido que ya está en destino (no haría falta copiar).</summary>
        public bool YaEnDestino => Estado == FileState.Igual
            || Estado == FileState.MismoHashDistintoNombre
          //  || Estado == FileState.MovidoORenombrado
            || Estado == FileState.DuplicadoAmbos
            || Estado == FileState.DuplicadoDestino;
    }
}


