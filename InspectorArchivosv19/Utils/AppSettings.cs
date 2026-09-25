using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using InspectorArchivos.Utils;

namespace InspectorArchivos.Utils
{
    /// <summary>
    /// Configuración persistente de la aplicación, guardada como JSON en
    /// %AppData%\InspectorArchivosv7\settings.json.
    /// Recuerda el orden/ancho de las columnas de la tabla comparativa (por nombre)
    /// y la última carpeta usada en los diálogos de selección.
    /// </summary>
    public class AppSettings
    {
        /// <summary>Última carpeta seleccionada (origen o destino) para los diálogos.</summary>
        public string LastFolder { get; set; } = "";

        /// <summary>Orden de visualización de cada columna, indexado por nombre de columna.</summary>
        public Dictionary<string, int> ColumnOrder { get; set; } = new Dictionary<string, int>();

        /// <summary>Ancho de cada columna, indexado por nombre de columna.</summary>
        public Dictionary<string, int> ColumnWidths { get; set; } = new Dictionary<string, int>();

        // ---------------------------------------------------------------------

        // Valores por omision persistentes en settings.json
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
        public bool MediaOnly { get; set; } = false;


        private const string FileName = "settings.json"; // JSON
        
        //private static string SettingsDir =>
        //    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InspectorArchivosv7");
        //private static string SettingsPath => Path.Combine(SettingsDir, "settings.json");
        private static string SettingsDir => AppContext.BaseDirectory;
        
        private static string SettingsPath => Path.Combine(SettingsDir, FileName);

        /// <summary>Carga la configuración desde disco; devuelve una nueva si no existe o falla.</summary>
        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (s != null)
                    {
                        s.ColumnOrder ??= new Dictionary<string, int>();
                        s.ColumnWidths ??= new Dictionary<string, int>();
                        return s;
                    }
                }
            }
            catch { /* configuración corrupta: se ignora y se usa una nueva */ }

            // Si no existe settings.json, inicializa desde DefaultsService si existe
            try
            {
                var defaults = DefaultsService.Load();
                var s2 = new AppSettings();
                s2.LastFolder = string.Empty;
                s2.ColumnOrder = new Dictionary<string, int>();
                s2.ColumnWidths = new Dictionary<string, int>();

                // aplicar por omision
                s2.CalcularHashBlake3 = defaults.CalcularHashBlake3;
                s2.SoloImagenes = defaults.SoloImagenes;
                s2.CarpetaDestinoPrioritaria = defaults.CarpetaDestinoPrioritaria;
                s2.TituloCarpeta = defaults.TituloCarpeta;
                s2.EleccionProcedencia = defaults.EleccionProcedencia;
                s2.ArchivosOrigen = defaults.ArchivosOrigen;
                s2.ArchivosDestino = defaults.ArchivosDestino;
                s2.PorFechas = defaults.PorFechas;
                s2.AnadirTitulo = defaults.AnadirTitulo;
                s2.Versionar = defaults.Versionar;
                s2.CarpetaPapelera = defaults.CarpetaPapelera;

                return s2;
            }
            catch { }

            return new AppSettings();
        }

        /// <summary>Guarda la configuración en disco.</summary>
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch { /* si no se puede guardar, no es crítico */ }
        }
    }
}

