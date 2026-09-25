using System;

namespace InspectorArchivos.Models
{
    public class Scan
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Scope { get; set; } 
    }
}
