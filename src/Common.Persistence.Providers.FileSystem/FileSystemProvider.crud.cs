//-------------------------------------------------------------------------------
// <copyright file="FileSystemProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.FileSystem
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Persistence.Contract;
    using Microsoft.Extensions.Logging;
    using Unity;

    public partial class FileSystemProvider<T> : ICrudStorageProvider<T> where T : IEntity
    {
        private readonly FileSystemStoreSettings storeSettings;
        private readonly ILogger<FileSystemProvider<T>> logger;
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, T> cache = new();
        private readonly object cacheLock = new();
        private volatile bool cacheLoaded = false;

        public FileSystemProvider(IServiceProvider serviceProvider, string name)
            : base(serviceProvider, name)
        {
            this.storeSettings = this.ConfigReader.ReadSettings<FileSystemStoreSettings>(name);
            this.logger = this.GetLogger<FileSystemProvider<T>>();
        }

        public FileSystemProvider(UnityContainer container, string name)
            : base(container, name)
        {
            this.storeSettings = this.ConfigReader.ReadSettings<FileSystemStoreSettings>(name);
            this.logger = this.GetLogger<FileSystemProvider<T>>();
        }

        public async Task<T?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            await this.EnsureCacheLoadedAsync(cancellationToken);

            lock (this.cacheLock)
            {
                return this.cache.TryGetValue(key, out var entity)
                    ? entity
                    : default(T);
            }
        }

        public async Task<IEnumerable<T>> GetManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            await this.EnsureCacheLoadedAsync(cancellationToken);

            lock (this.cacheLock)
            {
                return keys.Where(this.cache.ContainsKey).Select(key => this.cache[key]).ToList();
            }
        }

        public async Task<IEnumerable<T>> GetAllAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            await this.EnsureCacheLoadedAsync(cancellationToken);

            lock (this.cacheLock)
            {
                var values = this.cache.Values.AsEnumerable();
                if (predicate != null)
                {
                    var compiled = predicate.Compile();
                    values = values.Where(compiled);
                }

                return values.ToList();
            }
        }

        public async Task SaveAsync(string key, T entity, CancellationToken cancellationToken = default)
        {
            await this.EnsureCacheLoadedAsync(cancellationToken);

            lock (this.cacheLock)
            {
                this.cache[key] = entity;
            }

            await this.SaveCacheToFileAsync(cancellationToken);
        }

        public async Task SaveManyAsync(IEnumerable<KeyValuePair<string, T>> entities, CancellationToken cancellationToken = default)
        {
            await this.EnsureCacheLoadedAsync(cancellationToken);

            lock (this.cacheLock)
            {
                foreach (var (key, entity) in entities)
                {
                    this.cache[key] = entity;
                }
            }

            await this.SaveCacheToFileAsync(cancellationToken);
        }

        public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            await this.EnsureCacheLoadedAsync(cancellationToken);

            lock (this.cacheLock)
            {
                this.cache.Remove(key);
            }

            await this.SaveCacheToFileAsync(cancellationToken);
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            await this.EnsureCacheLoadedAsync(cancellationToken);

            lock (this.cacheLock)
            {
                return this.cache.ContainsKey(key);
            }
        }

        public async Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            var entities = await this.GetAllAsync(predicate, cancellationToken);
            return entities.Count();
        }

        public Task<long> ClearAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                lock (this.cacheLock)
                {
                    this.cache.Clear();
                }

                // Ensure the file is cleared as well
                if (File.Exists(this.storeSettings.FilePath))
                {
                    File.Delete(this.storeSettings.FilePath);
                }

                this.logger.LogDebug("Cleared cache and deleted file {FilePath}", this.storeSettings.FilePath);
                return 0L; // Return 0 as the count after clearing
            }, cancellationToken);
        }

        private async Task EnsureCacheLoadedAsync(CancellationToken cancellationToken)
        {
            if (this.cacheLoaded) return;

            await this.semaphore.WaitAsync(cancellationToken);
            try
            {
                if (this.cacheLoaded) return;

                await this.LoadCacheFromFileAsync(cancellationToken);
                this.cacheLoaded = true;
            }
            finally
            {
                this.semaphore.Release();
            }
        }

        private async Task LoadCacheFromFileAsync(CancellationToken cancellationToken)
        {
            try
            {
                var fileContent = await File.ReadAllBytesAsync(this.storeSettings.FilePath, cancellationToken);
                var data = await this.Serializer.DeserializeDictionaryAsync(fileContent, cancellationToken);

                lock (this.cacheLock)
                {
                    this.cache.Clear();
                    foreach (var (key, entity) in data)
                    {
                        this.cache[key] = entity;
                    }
                }

                this.logger.LogDebug("Loaded {Count} entities from {FilePath}", data, this.storeSettings.FilePath);
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "Failed to load cache from {FilePath}, starting with empty cache", this.storeSettings.FilePath);

                lock (this.cacheLock)
                {
                    this.cache.Clear();
                }
            }
        }

        private async Task SaveCacheToFileAsync(CancellationToken cancellationToken)
        {
            await this.semaphore.WaitAsync(cancellationToken);
            try
            {
                Dictionary<string, T> cacheSnapshot;
                lock (this.cacheLock)
                {
                    cacheSnapshot = new Dictionary<string, T>(this.cache);
                }

                // Atomic write using temp file + rename
                var tempFilePath = this.storeSettings.FilePath + ".tmp";
                var content = await this.Serializer.SerializeDictionaryAsync(cacheSnapshot, cancellationToken);
                await File.WriteAllBytesAsync(tempFilePath, content, cancellationToken);

                // Atomic rename - works on same filesystem
                File.Move(tempFilePath, this.storeSettings.FilePath, overwrite: true);

                this.logger.LogDebug("Saved {Count} entities to {FilePath}", cacheSnapshot.Count, this.storeSettings.FilePath);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to save cache to {FilePath}", this.storeSettings.FilePath);
                throw;
            }
            finally
            {
                this.semaphore.Release();
            }
        }

        private void EnsureDirectoryExists()
        {
            var directory = Path.GetDirectoryName(this.storeSettings.FilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                this.logger.LogDebug("Created directory {Directory}", directory);
            }
        }

        public void Dispose()
        {
            this.semaphore.Dispose();
        }
    }
}