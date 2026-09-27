using System;

namespace InspectorArchivosv19.Models
{
    public class ComparisonRow
    {
        public long RowId { get; set; }
        public string RelPath { get; set; }
        public string Filename { get; set; }
        public string StatusCode { get; set; }
        public long? OriginSize { get; set; }
        public long? DestSize { get; set; }
        public DateTime? OriginMtime { get; set; }
        public DateTime? DestMtime { get; set; }
        public string OriginHash { get; set; }
        public string DestHash { get; set; }
    }
}
