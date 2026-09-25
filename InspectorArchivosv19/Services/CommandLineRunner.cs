using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using InspectorArchivos.Database;
using InspectorArchivos.Forms;
using System.Runtime.InteropServices;

namespace InspectorArchivos.Services
{
    internal static class CommandLineRunner
    {

        public static int Run(string[] args)
        {

            if (args.Length == 0) return -1;
            if (args.Any(a => a is "--help" or "-h" or "/?")) { PrintHelp(); return 0; }
            try
            {
                var options = Parse(args);
                if (options.OnlyImages && options.OnlyNonImages) throw new ArgumentException("--only-images y --only-non-images no pueden usarse juntos.");
                if (options.Origin.Count == 0 && options.Destination.Count == 0) throw new ArgumentException("Indique --origin y/o --destination.");
                if (options.Fusion && (options.Origin.Count == 0 || options.Destination.Count == 0 || string.IsNullOrWhiteSpace(options.Trash)))
                    throw new ArgumentException("--fusion-fechas requiere --origin, --destination y --trash.");
                if (options.Origin.Concat(options.Destination).Any(p => !Directory.Exists(p) && !File.Exists(p)))
                    throw new ArgumentException("Una ruta indicada no existe.");

                MediaExtensionsService.RestrictToMedia = options.OnlyImages;
                FileEnumerator.OnlyNonMedia = options.OnlyNonImages;
                using var db = Program.OpenDatabase(options.DbPath);
                var repo = new Repository(db);
                if (options.Fusion)
                {
                    var fusion = new DateFusionService(repo).Execute(options.Origin, options.Destination, options.Trash, options.Hash, CancellationToken.None, Console.WriteLine);
                    Console.WriteLine($"Resultado: {fusion.ResultFolder}; pendientes: {fusion.PendingReview}.");
                    return 0;
                }
                var scanner = new Scanner(repo, options.Hash);
                if (options.Origin.Count > 0) scanner.ScanAsync(options.Origin, Models.ScanScope.origen, null, CancellationToken.None, Report).GetAwaiter().GetResult();
                if (options.Destination.Count > 0) scanner.ScanAsync(options.Destination, Models.ScanScope.destino, null, CancellationToken.None, Report).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("Error: " + ex.Message); Console.Error.WriteLine("Use --help para ver la sintaxis."); return 2; }
        }

        private static void Report(string path, Exception ex) => Console.Error.WriteLine($"{path}: {ex.Message}");

        private static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => ++i < args.Length ? args[i] : throw new ArgumentException($"Falta el valor de {a}.");

                bool NextBool(bool defaultValue)
                {
                    // Si el siguiente argumento existe y es "true"/"false", lo consume.
                    // Si no, no avanza el índice y usa el valor por defecto.
                    if (i + 1 < args.Length && bool.TryParse(args[i + 1], out bool parsed))
                    {
                        i++;
                        return parsed;
                    }
                    return defaultValue;
                }

                // Consume TODOS los argumentos siguientes que no sean otro parámetro
                // (no empiecen por "--"), permitiendo una lista de rutas.
                List<string> NextPathList()
                {
                    var list = new List<string>();
                    while (i + 1 < args.Length && !IsOptionFlag(args[i + 1]))
                    {
                        i++;
                        list.AddRange(SplitPaths(args[i]));
                    }
                    if (list.Count == 0) throw new ArgumentException($"Falta el valor de {a}.");
                    return list;
                }

                switch (a.ToLowerInvariant())
                {
                    case "--origin": o.Origin.AddRange(NextPathList()); break;
                    case "--destination": o.Destination.AddRange(NextPathList()); break;
                    case "--trash": o.Trash = Next(); break;
                    case "--db": o.DbPath = Next(); break;
                    case "--no-hash": o.Hash = !NextBool(true); break; // ojo con la inversión, ver nota
                    case "--only-images": o.OnlyImages = NextBool(true); break;
                    case "--only-non-images": o.OnlyNonImages = NextBool(true); break;
                    case "--fusion-fechas": o.Fusion = NextBool(true); break;
                    default: throw new ArgumentException($"Parámetro desconocido: {a}");
                }
            }
            o.Origin = o.Origin.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            o.Destination = o.Destination.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return o;
        }

        /// <summary>Indica si un token es un parámetro reconocido (empieza por "--"),
        /// para saber dónde termina una lista de rutas.</summary>
        private static bool IsOptionFlag(string token) => token.StartsWith("--", StringComparison.Ordinal);

        private static IEnumerable<string> SplitPaths(string value) => value.Split('|').Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim());
        private static void PrintHelp() => Console.WriteLine($"{MainForm.exeversion} --origin <ruta> [<ruta> ...] [--destination <ruta> [<ruta> ...]] [--db archivo.db] [--no-hash] [--only-images|--only-non-images] [--fusion-fechas --trash carpeta]");
        private sealed class Options { public List<string> Origin { get; set; } = new(); public List<string> Destination { get; set; } = new(); public string Trash { get; set; } = ""; public string DbPath { get; set; } = Program.DefaultPasswordJsonPath; public bool Hash { get; set; } = true; public bool OnlyImages { get; set; } public bool OnlyNonImages { get; set; } public bool Fusion { get; set; } }
    }
}
