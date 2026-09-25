using System;

namespace InspectorArchivos.Models
{
    /// <summary>
    /// Indica a qué conjunto pertenece un escaneo: carpetas de origen o de destino.
    /// </summary>
    public enum ScanScope
    {
        origen,
        destino
    }

    /// <summary>
    /// Estado de un escaneo completo.
    /// </summary>
    public enum ScanStatus
    {
        Pending,
        Running,
        Completed,
        Cancelled,
        Failed
    }

    /// <summary>
    /// Representa un escaneo de una o varias carpetas raíz.
    /// </summary>
    public class ScanRecord
    {
        public long Id { get; set; }
        public Guid Guid { get; set; }
        public string RootPaths { get; set; }
        public ScanScope Scope { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime FinishedAt { get; set; }
        public long FileCount { get; set; }
        public long FolderCount { get; set; }
        public long ErrorCount { get; set; }
        public ScanStatus Status { get; set; }
        public string Notes { get; set; }

        public string ScopeLabel => Scope == ScanScope.origen ? "origen" : "destino";
        public string StatusLabel => Status.ToString();
    }
}

