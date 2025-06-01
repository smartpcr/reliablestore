//-------------------------------------------------------------------------------
// <copyright file="FileStore.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Tx;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;

    public class FileStore<T> : IRepository<T>, ITransactionalResource where T : class
    {
        private readonly string filePath;
        private readonly ILogger<FileStore<T>> logger;
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
        private Dictionary<string, T> cache;
        private readonly object cacheLock = new object();

        public FileStore(string filePath, ILogger<FileStore<T>> logger)
        {
            this.filePath = filePath;
            this.logger = logger;
            this.EnsureFileExists();
            this.LoadCache();
        }

        private void EnsureFileExists()
        {
            if (!File.Exists(this.filePath))
            {
                var directory = Path.GetDirectoryName(this.filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(this.filePath, JsonConvert.SerializeObject(new Dictionary<string, T>()));
            }
        }

        private void LoadCache()
        {
            try
            {
                var json = File.ReadAllText(this.filePath);
                this.cache = JsonConvert.DeserializeObject<Dictionary<string, T>>(json) ?? new Dictionary<string, T>();
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to load cache from file {FilePath}", this.filePath);
                this.cache = new Dictionary<string, T>();
            }
        }

        private async Task SaveToFileAsync()
        {
            try
            {
                var json = JsonConvert.SerializeObject(this.cache, Formatting.Indented);
                await File.WriteAllTextAsync(this.filePath, json);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to save to file {FilePath}", this.filePath);
                throw;
            }
        }

        public async Task<T> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            await this.semaphore.WaitAsync(cancellationToken);
            try
            {
                lock (this.cacheLock)
                {
                    return this.cache.TryGetValue(key, out var value) ? value : null;
                }
            }
            finally
            {
                this.semaphore.Release();
            }
        }

        public async Task<IEnumerable<T>> GetManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            await this.semaphore.WaitAsync(cancellationToken);
            try
            {
                lock (this.cacheLock)
                {
                    return keys.Select(key => this.cache.TryGetValue(key, out var value) ? value : null)
                               .Where(item => item != null);
                }
            }
            finally
            {
                this.semaphore.Release();
            }
        }

        public async Task<IEnumerable<T>> GetAllAsync(Func<T, bool> predicate = null, CancellationToken cancellationToken = default)
        {
            await this.semaphore.WaitAsync(cancellationToken);
            try
            {
                lock (this.cacheLock)
                {
                    var values = this.cache.Values;
                    return predicate != null ? values.Where(predicate) : values;
                }
            }
            finally
            {
                this.semaphore.Release();
            }
        }

        public async Task<T> SaveAsync(string key, T entity, CancellationToken cancellationToken = default)
        {
            await this.semaphore.WaitAsync(cancellationToken);
            try
            {
                lock (this.cacheLock)
                {
                    this.cache[key] = entity;
                }
                await this.SaveToFileAsync();
                return entity;
            }
            finally
            {
                this.semaphore.Release();
            }
        }

        public async Task SaveManyAsync(IDictionary<string, T> entities, CancellationToken cancellationToken = default)
        {
            await this.semaphore.WaitAsync(cancellationToken);
            try
            {
                lock (this.cacheLock)
                {
                    foreach (var kvp in entities)
                    {
                        this.cache[kvp.Key] = kvp.Value;
                    }
                }
                await this.SaveToFileAsync();
            }
            finally
            {
                this.semaphore.Release();
            }
        }

        public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            await this.semaphore.WaitAsync(cancellationToken);
            try
            {
                bool removed;
                lock (this.cacheLock)
                {
                    removed = this.cache.Remove(key);
                }
                if (removed)
                {
                    await this.SaveToFileAsync();
                }
                return removed;
            }
            finally
            {
                this.semaphore.Release();
            }
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            await this.semaphore.WaitAsync(cancellationToken);
            try
            {
                lock (this.cacheLock)
                {
                    return this.cache.ContainsKey(key);
                }
            }
            finally
            {
                this.semaphore.Release();
            }
        }

        // ITransactionalResource implementation
        public string ResourceId => $"FileStore:{this.filePath}";

        public async Task<bool> PrepareAsync(ITransaction transaction, CancellationToken cancellationToken = default)
        {
            // For file-based storage, validate file accessibility and basic integrity
            try
            {
                await this.semaphore.WaitAsync(cancellationToken);
                try
                {
                    // Verify file is accessible and writable
                    var directory = Path.GetDirectoryName(this.filePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        return false;
                    }
                    
                    // Test write access
                    var testPath = this.filePath + ".test";
                    await File.WriteAllTextAsync(testPath, "{}", cancellationToken);
                    File.Delete(testPath);
                    
                    return true;
                }
                finally
                {
                    this.semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to prepare FileStore for transaction {TransactionId}", transaction.TransactionId);
                return false;
            }
        }

        public async Task CommitAsync(ITransaction transaction, CancellationToken cancellationToken = default)
        {
            // Ensure changes are persisted to file
            await this.SaveToFileAsync();
        }

        public Task RollbackAsync(ITransaction transaction, CancellationToken cancellationToken = default)
        {
            // Reload from file to discard in-memory changes
            this.LoadCache();
            return Task.CompletedTask;
        }

        public Task CreateSavepointAsync(ITransaction transaction, ISavepoint savepoint, CancellationToken cancellationToken = default)
        {
            // For simplicity, we don't implement savepoints in file store
            return Task.CompletedTask;
        }

        public Task RollbackToSavepointAsync(ITransaction transaction, ISavepoint savepoint, CancellationToken cancellationToken = default)
        {
            // For simplicity, we don't implement savepoints in file store
            return Task.CompletedTask;
        }

        public Task DiscardSavepointDataAsync(ITransaction transaction, ISavepoint savepointToDiscard, CancellationToken cancellationToken = default)
        {
            // For simplicity, we don't implement savepoints in file store
            return Task.CompletedTask;
        }
    }
}