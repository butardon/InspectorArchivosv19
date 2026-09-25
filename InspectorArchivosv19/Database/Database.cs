using System;
using System.IO;
using System.Text.Json;
using Npgsql;

namespace InspectorArchivos.Database
{
    /// <summary>
    /// Gestiona la conexión con PostgreSQL.
    /// El esquema y las tablas se gestionan fuera de esta clase; deben existir
    /// previamente en la base de datos PostgreSQL.
    /// </summary>
    public class Database : IDisposable
    {
        private const string Host = "192.168.1.167";
        private const int Port = 5435;
        private const string DatabaseName = "mi_nas_db";
        private const string Username = "postgres";
        private const int ConnectionTimeout = 15;
        private const int CommandTimeout = 60;

        private readonly NpgsqlConnection _connection;

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

            _connection = new NpgsqlConnection(builder.ConnectionString);
            _connection.Open();
        }

        public NpgsqlConnection Connection => _connection;

        private static string LoadPassword(string jsonPath)
        {
            if (string.IsNullOrWhiteSpace(jsonPath))
                throw new ArgumentException("No se ha indicado el archivo JSON de contraseña.", nameof(jsonPath));

            if (!File.Exists(jsonPath))
                throw new FileNotFoundException($"No se encontró el archivo JSON de contraseña: {jsonPath}", jsonPath);

            string json = File.ReadAllText(jsonPath);
            using JsonDocument document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("password", out JsonElement passwordElement))
                throw new InvalidOperationException($"El archivo JSON '{jsonPath}' no contiene la propiedad 'password'.");

            string  ? password = passwordElement.GetString();
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException($"La propiedad 'password' del archivo '{jsonPath}' está vacía.");

            return password;
        }

        public void Execute(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw new ArgumentException("La sentencia SQL no puede estar vacía.", nameof(sql));

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = CommandTimeout;
            cmd.ExecuteNonQuery();
        }

        public NpgsqlTransaction BeginTransaction() => _connection.BeginTransaction();

        public void Dispose()
        {
            try { _connection?.Close(); } catch { }
            _connection?.Dispose();
        }
    }
}
