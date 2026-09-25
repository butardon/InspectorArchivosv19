//  Program.cs

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using InspectorArchivos.Database;
using InspectorArchivos.Services;
using System.Text.Json; // Agregar para deserialización JSON
using Npgsql; // Agregar directiva using para Npgsql

namespace InspectorArchivos
{
    internal static class Program
    {
        // Archivo JSON con la contraseña de PostgreSQL. Se puede cambiar por configuración.
        public const string DefaultPasswordJsonPath = "database.json";

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                bool attached = NativeConsole.TryAttachToParentConsole();
                if (attached)
                {
                    // Evita que la salida quede pegada al prompt del CMD
                    Console.WriteLine();
                }

                int commandResult = CommandLineRunner.Run(args);

                if (attached)
                {
                    Console.Out.Flush();
                    Console.Error.Flush();
                    NativeConsole.Detach();
                }

                return commandResult;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new Forms.MainForm(DefaultPasswordJsonPath));
            return 0;
        }

        /// <summary>Abre la conexión PostgreSQL usando la contraseña del JSON indicado.</summary>
        public static Database.Database OpenDatabase(string passwordJsonPath)
        {
            return new Database.Database(passwordJsonPath);
        }
    }

    /// <summary>
    /// Permite que una aplicación WinExe escriba en la consola (CMD/PowerShell)
    /// desde la que fue invocada, adjuntándose a ella y redirigiendo los streams
    /// estándar de .NET.
    /// </summary>
    internal static class NativeConsole
    {
        private const int ATTACH_PARENT_PROCESS = -1;

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        /// <summary>
        /// Intenta adjuntarse a la consola del proceso padre (p. ej. el CMD que
        /// lanzó el ejecutable) y redirige Console.Out/Error/In hacia ella.
        /// Devuelve false si el proceso no fue lanzado desde una consola
        /// (por ejemplo, doble clic en el .exe).
        /// </summary>
        public static bool TryAttachToParentConsole()
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
                return false;

            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(stdout);

            var stderr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
            Console.SetError(stderr);

            var stdin = new StreamReader(Console.OpenStandardInput());
            Console.SetIn(stdin);

            return true;
        }

        public static void Detach() => FreeConsole();
    }
}

public class Database : IDisposable
{
    // Definir constantes o propiedades para los valores de conexión
    private const string Host = "localhost";
    private const int Port = 5432;
    private const string DatabaseName = "mi_basededatos";
    private const string Username = "mi_usuario";
    private const int ConnectionTimeout = 15;
    private const int CommandTimeout = 30;

    private readonly string _connString;
    public Database(string passwordJsonPath)
    {
        string password = LoadPassword(passwordJsonPath);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = DatabaseName,
            Username = Username,
            Password = password,
            Timeout = ConnectionTimeout,
            CommandTimeout = CommandTimeout
        };
        _connString = builder.ConnectionString;
    }

    // Método privado para cargar la contraseña desde el archivo JSON
    private static string LoadPassword(string passwordJsonPath)
    {
        if (!File.Exists(passwordJsonPath))
            throw new FileNotFoundException($"No se encontró el archivo de contraseña: {passwordJsonPath}");

        string json = File.ReadAllText(passwordJsonPath);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("password", out var passwordElement))
            throw new InvalidDataException("El archivo JSON no contiene la propiedad 'password'.");

        return passwordElement.GetString();
    }

    public NpgsqlConnection CreateOpenConnection()
    {
        var c = new NpgsqlConnection(_connString);
        c.Open();
        return c;
    }

    public void Execute(string sql)
    {
        using var conn = CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = CommandTimeout;
        cmd.ExecuteNonQuery();
    }

    public NpgsqlTransaction BeginTransaction(out NpgsqlConnection connection)
    {
        connection = CreateOpenConnection();
        return connection.BeginTransaction();
    }

    public void Dispose()
    {
        // nada que cerrar aquí; las conexiones se cierran por operación
    }
}
