using System;
using System.IO;
using System.Text.Json;

namespace InspectorArchivos.Utils
{
    public class Defaults
        {
        public bool CalcularHashBlake3 { get; set; } = true;
        public bool SoloImagenes { get; set; } = false;
        public bool SoloNoImagenes { get; set; } = false;
        public string CarpetaDestinoPrioritaria { get; set; } = "";
        public string TituloCarpeta { get; set; } = "";
        public string EleccionProcedencia { get; set; } = "origen";
        public bool ArchivosOrigen { get; set; } = true;
        public bool ArchivosDestino { get; set; } = true;
        public bool PorFechas { get; set; } = false;
        public bool AnadirTitulo { get; set; } = false;
        public bool Versionar { get; set; } = false;
        public string CarpetaPapelera { get; set; } = "";
    }

    public static class DefaultsService
    {
        private const string FileName = "Defaults.txt"; // JSON
        private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, FileName);

        public static Defaults Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var d = JsonSerializer.Deserialize<Defaults>(json);
                    if (d != null) return d;
                }
            }
            catch { }
            // return defaults if missing/corrupt
            return new Defaults();
        }

        public static void Save(Defaults d)
        {
            try
            {
                var json = JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }
    }
}

