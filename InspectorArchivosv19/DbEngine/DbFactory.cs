using System;
using System.Threading.Tasks;
using Npgsql;

namespace InspectorArchivos.Database
{
    public static class DbFactory
    {
        private static NpgsqlDataSource _dataSource;

        public static void Initialize(string connectionString)
        {
            if (_dataSource != null)
                return;

            var builder = new NpgsqlDataSourceBuilder(connectionString);
            _dataSource = builder.Build();
        }

        public static NpgsqlConnection CreateConnection()
        {
            if (_dataSource == null)
                throw new InvalidOperationException("DbFactory no se ha inicializado. Llama a DbFactory.Initialize(connectionString) antes de crear conexiones.");

            return _dataSource.CreateConnection();
        }

        public static async Task ShutdownAsync()
        {
            if (_dataSource != null)
            {
                await _dataSource.DisposeAsync();
                _dataSource = null;
            }
        }
    }
}