//-------------------------------------------------------------------------------
// <copyright file="FileExtensions.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    using System.IO;
    using System.Threading.Tasks;
    using System.Threading;

    public static class FileExtensions
    {

        public static async Task<byte[]> ReadAllBytesAsync(string filePath, CancellationToken token)
        {
            using (var fs = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true))
            {
                var bytes = new byte[fs.Length];
                var offset = 0;
                var bytesRemaining = (int)fs.Length;

                while (bytesRemaining > 0)
                {
                    var bytesRead = await fs.ReadAsync(bytes, offset, bytesRemaining, token).ConfigureAwait(false);
                    if (bytesRead == 0)
                        break; // End of file reached unexpectedly
                    offset += bytesRead;
                    bytesRemaining -= bytesRead;
                }

                return bytes;
            }
        }

        public static async Task WriteAllBytesAsync(string filePath, byte[] value, CancellationToken token = default)
        {
            // Open the file for asynchronous write. FileMode.Create will create a new file or overwrite an existing one.
            using (var fs = new FileStream(
                filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                // Write the entire byte array asynchronously.
                await fs.WriteAsync(value, 0, value.Length, token).ConfigureAwait(false);

                // Optionally flush to ensure all data is written to disk.
                // await fs.FlushAsync(token).ConfigureAwait(false);
                // <-- no FlushAsync
                // dispose will close the handle, but let the OS schedule the actual disk write
                // That change alone will eliminate the multi-hundred-ms delay you're seeing on the cluster share.
            }
        }

        public static async Task<string> ReadAllTextAsync(string filePath, CancellationToken token = default)
        {
            // Open the file for asynchronous read
            using (var reader = new StreamReader(
                new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true)))
            {
                // Read the entire file content asynchronously
                return await reader.ReadToEndAsync().ConfigureAwait(false);
            }
        }

        public static async Task WriteAllTextAsync(string filePath, string contents, CancellationToken token = default)
        {
            // Open the file for asynchronous write
            using (var writer = new StreamWriter(
                new FileStream(
                    filePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    useAsync: true)))
            {
                // Write the entire file content asynchronously
                await writer.WriteAsync(contents).ConfigureAwait(false);
            }
        }
    }
}