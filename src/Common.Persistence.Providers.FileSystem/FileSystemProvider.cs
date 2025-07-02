//-------------------------------------------------------------------------------
// <copyright file="FileSystemProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.FileSystem
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Persistence.Contract;
    using Microsoft.Extensions.Logging;
    using Unity;

    /// <summary>
    /// File-based persistence provider that stores each entity in a separate file.
    /// Follows the pattern from CsvFileCache for better performance and concurrency.
    /// </summary>
    public partial class FileSystemProvider<T> : ICrudStorageProvider<T> where T : IEntity
    {
        private readonly FileSystemStoreSettings storeSettings;
        private readonly ILogger<FileSystemProvider<T>> logger;
        
        // Per-file locking instead of global lock for better concurrency
        private readonly ConcurrentDictionary<string, SemaphoreSlim> fileLocks = new();
        private readonly int maxConcurrentFiles;
        
        // Cache for directory existence to avoid repeated I/O
        private volatile bool directoryEnsured = false;
        private readonly object directoryLock = new();

        public FileSystemProvider(IServiceProvider serviceProvider, string name)
            : base(serviceProvider, name)
        {
            this.storeSettings = this.ConfigReader.ReadSettings<FileSystemStoreSettings>(name);
            this.logger = this.GetLogger<FileSystemProvider<T>>();
            this.maxConcurrentFiles = this.storeSettings.MaxConcurrentFiles ?? Environment.ProcessorCount * 2;
            this.EnsureDirectoryExists();
        }

        public FileSystemProvider(UnityContainer container, string name)
            : base(container, name)
        {
            this.storeSettings = this.ConfigReader.ReadSettings<FileSystemStoreSettings>(name);
            this.logger = this.GetLogger<FileSystemProvider<T>>();
            this.maxConcurrentFiles = this.storeSettings.MaxConcurrentFiles ?? Environment.ProcessorCount * 2;
            this.EnsureDirectoryExists();
        }

        public async Task<T?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            var filePath = this.GetFilePath(key);
            
            if (!File.Exists(filePath))
            {
                this.logger.LogDebug("Entity {Key} not found at {FilePath}", key, filePath);
                return default(T);
            }

            // Get file-specific lock for reading
            var fileLock = this.GetOrCreateFileLock(filePath);
            
            await fileLock.WaitAsync(cancellationToken);
            try
            {
                // Double-check file exists after acquiring lock
                if (!File.Exists(filePath))
                {
                    return default(T);
                }

                // Read file with retry logic for transient errors
                var fileContent = await this.ReadFileWithRetryAsync(filePath, cancellationToken);
                if (fileContent == null)
                {
                    return default(T);
                }

                var entity = await this.Serializer.DeserializeAsync(fileContent, cancellationToken);
                this.logger.LogDebug("Retrieved entity {Key} from {FilePath}", key, filePath);
                return entity;
            }
            finally
            {
                fileLock.Release();
                this.CleanupFileLockIfPossible(filePath);
            }
        }

        public async Task<IEnumerable<T>> GetManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            var tasks = keys.Select(key => this.GetAsync(key, cancellationToken));
            var results = await Task.WhenAll(tasks);
            return results.Where(r => r != null).Cast<T>();
        }

        public async Task<IEnumerable<T>> GetAllAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            this.EnsureDirectoryExists();
            
            var directory = this.GetRootDirectory();
            if (!Directory.Exists(directory))
            {
                return Enumerable.Empty<T>();
            }

            var files = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories);
            
            // Process files in parallel with limited concurrency
            var results = new ConcurrentBag<T>();
            var semaphore = new SemaphoreSlim(this.maxConcurrentFiles);
            
            var tasks = files.Select(async file =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var key = this.FilePathToKey(file);
                    var entity = await this.GetAsync(key, cancellationToken);
                    if (entity != null)
                    {
                        if (predicate == null || predicate.Compile()(entity))
                        {
                            results.Add(entity);
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.logger.LogWarning(ex, "Failed to read file {FilePath}", file);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            return results.ToList();
        }

        public async Task SaveAsync(string key, T entity, CancellationToken cancellationToken = default)
        {
            this.EnsureDirectoryExists();
            
            var filePath = this.GetFilePath(key);
            var directory = Path.GetDirectoryName(filePath);
            
            // Create subdirectory if needed (thread-safe)
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Get file-specific lock for writing
            var fileLock = this.GetOrCreateFileLock(filePath);
            
            await fileLock.WaitAsync(cancellationToken);
            try
            {
                var content = await this.Serializer.SerializeAsync(entity, cancellationToken);
                
                // Write to temp file first, then atomic rename
                await this.WriteFileAtomicAsync(filePath, content, cancellationToken);
                
                this.logger.LogDebug("Saved entity {Key} to {FilePath}", key, filePath);
            }
            finally
            {
                fileLock.Release();
                this.CleanupFileLockIfPossible(filePath);
            }
        }

        public async Task SaveManyAsync(IEnumerable<KeyValuePair<string, T>> entities, CancellationToken cancellationToken = default)
        {
            // Process saves in parallel with limited concurrency
            var semaphore = new SemaphoreSlim(this.maxConcurrentFiles);
            
            var tasks = entities.Select(async kvp =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    await this.SaveAsync(kvp.Key, kvp.Value, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }

        public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            var filePath = this.GetFilePath(key);
            
            if (!File.Exists(filePath))
            {
                this.logger.LogDebug("Entity {Key} not found for deletion at {FilePath}", key, filePath);
                return;
            }

            await this.DeleteFileWithRetryAsync(filePath, cancellationToken);
            this.logger.LogDebug("Deleted entity {Key} from {FilePath}", key, filePath);
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            var filePath = this.GetFilePath(key);
            return await Task.FromResult(File.Exists(filePath));
        }

        public async Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            if (predicate == null)
            {
                // Fast path for count without predicate
                var directory = this.GetRootDirectory();
                if (!Directory.Exists(directory))
                {
                    return 0;
                }
                
                var files = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories);
                return files.Length;
            }
            
            // Slower path with predicate
            var entities = await this.GetAllAsync(predicate, cancellationToken);
            return entities.Count();
        }

        public async Task<long> ClearAsync(CancellationToken cancellationToken = default)
        {
            var directory = this.GetRootDirectory();
            if (!Directory.Exists(directory))
            {
                return 0;
            }

            var files = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories);
            long deletedCount = 0;

            // Delete files in parallel with limited concurrency
            var semaphore = new SemaphoreSlim(this.maxConcurrentFiles);
            
            var tasks = files.Select(async file =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    await this.DeleteFileWithRetryAsync(file, cancellationToken);
                    Interlocked.Increment(ref deletedCount);
                }
                catch (Exception ex)
                {
                    this.logger.LogWarning(ex, "Failed to delete file {FilePath}", file);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            
            this.logger.LogDebug("Cleared {Count} entities from {Directory}", deletedCount, directory);
            return deletedCount;
        }

        public void Dispose()
        {
            foreach (var (_, semaphore) in this.fileLocks)
            {
                semaphore.Dispose();
            }
            this.fileLocks.Clear();
        }

        #region Helper Methods

        private string GetRootDirectory()
        {
            return this.storeSettings.FolderPath;
        }

        private string GetFilePath(string key)
        {
            // Sanitize key to create a valid file path
            var sanitizedKey = this.SanitizeKey(key);
            var directory = this.GetRootDirectory();
            
            // Use subdirectories based on first 2 chars of key for better file system performance
            if (this.storeSettings.UseSubdirectories && sanitizedKey.Length >= 2)
            {
                var subDir = sanitizedKey.Substring(0, 2);
                return Path.Combine(directory, subDir, $"{sanitizedKey}.json");
            }
            
            return Path.Combine(directory, $"{sanitizedKey}.json");
        }

        private string FilePathToKey(string filePath)
        {
            var directory = this.GetRootDirectory();
            var relativePath = Path.GetRelativePath(directory, filePath);
            
            // Remove .json extension and subdirectory structure
            var key = Path.GetFileNameWithoutExtension(relativePath);
            return key;
        }

        private string SanitizeKey(string key)
        {
            // Replace invalid file path characters with underscores
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = key;
            
            foreach (var c in invalidChars)
            {
                sanitized = sanitized.Replace(c, '_');
            }
            
            // Also replace some additional characters that could cause issues
            sanitized = sanitized.Replace('/', '_').Replace('\\', '_');
            
            return sanitized;
        }

        private void EnsureDirectoryExists()
        {
            if (this.directoryEnsured) return;

            lock (this.directoryLock)
            {
                if (this.directoryEnsured) return;

                var directory = this.GetRootDirectory();
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    this.logger.LogDebug("Created directory {Directory}", directory);
                }
                
                this.directoryEnsured = true;
            }
        }

        private SemaphoreSlim GetOrCreateFileLock(string filePath)
        {
            return this.fileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
        }

        private void CleanupFileLockIfPossible(string filePath)
        {
            // Clean up locks for non-existent files to prevent memory leak
            if (!File.Exists(filePath) && this.fileLocks.TryGetValue(filePath, out var semaphore))
            {
                if (semaphore.CurrentCount == 1) // Not in use
                {
                    this.fileLocks.TryRemove(filePath, out _);
                    semaphore.Dispose();
                }
            }
        }

        private async Task<byte[]?> ReadFileWithRetryAsync(string filePath, CancellationToken cancellationToken)
        {
            var maxRetries = this.storeSettings.MaxRetries;
            var retryDelayMs = this.storeSettings.RetryDelayMs;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    // Use FileShare.ReadWrite to allow concurrent access
                    using var fs = new FileStream(
                        filePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        bufferSize: 4096,
                        useAsync: true);

                    var bytes = new byte[fs.Length];
                    var offset = 0;
                    var bytesRemaining = (int)fs.Length;

                    while (bytesRemaining > 0)
                    {
                        var bytesRead = await fs.ReadAsync(bytes, offset, bytesRemaining, cancellationToken);
                        if (bytesRead == 0)
                            break;
                        offset += bytesRead;
                        bytesRemaining -= bytesRead;
                    }

                    return bytes;
                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    // File might be temporarily locked, retry
                    await Task.Delay(retryDelayMs * (i + 1), cancellationToken);
                }
                catch (FileNotFoundException)
                {
                    return null;
                }
            }

            return null;
        }

        private async Task WriteFileAtomicAsync(string filePath, byte[] data, CancellationToken cancellationToken)
        {
            var maxRetries = this.storeSettings.MaxRetries;
            var retryDelayMs = this.storeSettings.RetryDelayMs;

            var tempFile = filePath + ".tmp." + Guid.NewGuid().ToString("N");

            try
            {
                // Write to temp file first
                using (var fs = new FileStream(
                    tempFile,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    useAsync: true))
                {
                    await fs.WriteAsync(data, 0, data.Length, cancellationToken);
                }

                // Atomic rename with retry
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        // Delete existing file if present
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                        }

                        File.Move(tempFile, filePath);
                        break;
                    }
                    catch (IOException) when (i < maxRetries - 1)
                    {
                        await Task.Delay(retryDelayMs * (i + 1), cancellationToken);
                    }
                }
            }
            finally
            {
                // Clean up temp file if it still exists
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        private async Task DeleteFileWithRetryAsync(string filePath, CancellationToken cancellationToken)
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            var maxRetries = this.storeSettings.MaxRetries + 2; // Extra retries for delete
            var retryDelayMs = this.storeSettings.RetryDelayMs;

            var fileLock = this.GetOrCreateFileLock(filePath);

            await fileLock.WaitAsync(cancellationToken);
            try
            {
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        File.Delete(filePath);

                        // Verify deletion
                        if (!File.Exists(filePath))
                        {
                            return;
                        }
                    }
                    catch (IOException) when (i < maxRetries - 1)
                    {
                        // File might be in use, retry
                    }
                    catch (UnauthorizedAccessException) when (i < maxRetries - 1)
                    {
                        // Access denied, retry
                    }

                    await Task.Delay(retryDelayMs * (i + 1), cancellationToken);
                }
                
                // Final attempt
                if (File.Exists(filePath))
                {
                    throw new IOException($"Failed to delete file '{filePath}' after {maxRetries} attempts.");
                }
            }
            finally
            {
                fileLock.Release();
                this.CleanupFileLockIfPossible(filePath);
            }
        }

        #endregion
    }
}