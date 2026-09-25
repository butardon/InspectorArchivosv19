using System;

namespace InspectorArchivos.Models
{
    /// <summary>
    /// Representa un error de acceso registrado durante un escaneo (tabla ScanErrors).
    /// </summary>
    public class ScanErrorRecord
    {
        public long Id { get; set; }
        public long ScanId { get; set; }
        public string Path { get; set; }
        public string Error { get; set; }
        public DateTime OccurredAt { get; set; }
    }
}

