using System;
using System.IO;

namespace InspectorArchivos.Models
{
    /// <summary>
    /// Estados posibles de un archivo al comparar dos escaneos.
    /// Los valores en español coinciden con los que genera el SQL de
    /// Repository.BuildComparison y ComparisonGridQueryService.
    /// </summary>
    public enum FileState
    {
        Desconocido,
        Igual,
        MismoHashDistintoNombre,
        SoloOrigen,
        SoloDestino,
        DuplicadoOrigen,
        DuplicadoDestino,
        DuplicadoAmbos
    }

    /// <summary>
    /// Fila de comparación entre dos escaneos. Contiene TODAS las propiedades
    /// necesarias tanto para el código existente (MainForm, Repository,
    /// ComparisonGridQueryService) como para el nuevo sistema de grid SQL
    /// paginado (ComparisonGridView, ComparisonRepository).
    /// </summary>
    public class ComparisonRow
    {
        // ── Identificadores ──
        public long RowId { get; set; }
        public long? FileIdOrigen { get; set; }
        public long? FileIdDestino { get; set; }

        // ── Estado ──
        public FileState Estado { get; set; }
        public string EstadoLabel => Estado.ToString();
        public bool EsDuplicado { get; set; }

        // ── Rutas ──
        public string RutaOrigen { get; set; }
        public string RutaDestino { get; set; }
        public string CarpetaPadreOrigen { get; set; }
        public string CarpetaPadreDestino { get; set; }

        // ── Nombre y extensión ──
        public string Nombre { get; set; }
        public string Extension { get; set; }

        // ── Tamaños ──
        public long? TamanoOrigen { get; set; }
        public long? TamanoDestino { get; set; }

        // ── Fechas ──
        public DateTime? FechaCreacionOrigen { get; set; }
        public DateTime? FechaCreacionDestino { get; set; }
        public DateTime? FechaModOrigen { get; set; }
        public DateTime? FechaModDestino { get; set; }

        // ── Atributos ──
        public FileAttributes? AtributosOrigen { get; set; }
        public FileAttributes? AtributosDestino { get; set; }

        // ── Hashes ──
        public string HashOrigen { get; set; }
        public string HashDestino { get; set; }
        public string Hash { get; set; }
        public string Fingerprint { get; set; }

        // ── Equipo y revisión ──
        public string NombrePcOrigen { get; set; }
        public string NombrePcDestino { get; set; }
        public string NombrePc { get; set; }
        public string RevisadoOrigen { get; set; }
        public string RevisadoDestino { get; set; }
        /// <summary>Procedencia del archivo: 'origen' o 'destino'.</summary>
        public string ProcedenciaArchivo { get; set; }

        // ── Duplicados ──
        public int RepeticionesOrigen { get; set; }
        public int RepeticionesDestino { get; set; }
        public int? Candidatura { get; set; }

        // ── Selección (UI) ──
        public bool Selected { get; set; }

        // ════════════════════════════════════════════════════════════
        //  Propiedades de compatibilidad para ComparisonGridView /
        //  ComparisonRepository (sistema de grid SQL paginado)
        // ════════════════════════════════════════════════════════════

        /// <summary>Ruta relativa (alias de compatibilidad).</summary>
        public string RelPath
        {
            get => RutaOrigen ?? RutaDestino ?? Nombre ?? string.Empty;
            set { /* asignación directa через RutaOrigen no aplica; lectura calculada */ }
        }

        /// <summary>Nombre del archivo (alias de compatibilidad).</summary>
        public string Filename
        {
            get => Nombre;
            set => Nombre = value;
        }

        /// <summary>Estado (alias de compatibilidad para el grid paginado).</summary>
        public FileState State
        {
            get => Estado;
            set => Estado = value;
        }

        /// <summary>Código de estado como texto (alias de EstadoLabel).</summary>
        public string StatusCode => EstadoLabel;

        /// <summary>Tamaño origen (alias de compatibilidad).</summary>
        public long? OriginSize
        {
            get => TamanoOrigen;
            set => TamanoOrigen = value;
        }

        /// <summary>Tamaño destino (alias de compatibilidad).</summary>
        public long? DestSize
        {
            get => TamanoDestino;
            set => TamanoDestino = value;
        }

        /// <summary>Fecha modificación origen (alias de compatibilidad).</summary>
        public DateTime? OriginMtime
        {
            get => FechaModOrigen;
            set => FechaModOrigen = value;
        }

        /// <summary>Fecha modificación destino (alias de compatibilidad).</summary>
        public DateTime? DestMtime
        {
            get => FechaModDestino;
            set => FechaModDestino = value;
        }

        /// <summary>Hash origen (alias de compatibilidad).</summary>
        public string OriginHash
        {
            get => HashOrigen;
            set => HashOrigen = value;
        }

        /// <summary>Hash destino (alias de compatibilidad).</summary>
        public string DestHash
        {
            get => HashDestino;
            set => HashDestino = value;
        }
    }
}
