using System;
using System.IO;
using Blake3;

namespace InspectorArchivos.Services
{
    /// <summary>
    /// Wrapper aislado sobre el paquete NuGet Blake3 (Blake3.NET de xoofx).
    /// Si la API del paquete cambia, solo hay que tocar esta clase.
    /// Calcula el hash BLAKE3 de un archivo leyéndolo en bloques (streaming)
    /// para no cargar archivos grandes enteros en memoria.
    /// </summary>
    public static class Blake3Hasher
    {
        private const int BufferSize = 81920; // 80 KB por lectura

        /// <summary>Devuelve el hash BLAKE3 en hexadecimal (64 caracteres) del archivo indicado.</summary>
        public static string HashFile(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.SequentialScan);
            return HashStream(fs);
        }

        /// <summary>
        /// Versión con cancelación. Lanza OperationCanceledException si se cancela.
        /// </summary>
        public static string HashFile(string path, System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.SequentialScan);
            return HashStream(fs, cancellationToken);
        }

        private static string HashStream(Stream stream, System.Threading.CancellationToken ct = default)
        {
            // Blake3.Hasher es un struct desechable. Se usa incrementalmente.
            using var hasher = Hasher.New();
            var buffer = new byte[BufferSize];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                hasher.Update(buffer.AsSpan(0, read));
            }
            return hasher.Finalize().ToString();
        }
    }
}

