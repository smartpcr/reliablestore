//-------------------------------------------------------------------------------
// <copyright file="ClusterRegistryProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Runtime.Versioning;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Persistence.Contract;
    using Common.Persistence.Providers.ClusterRegistry.Registry;
    using Microsoft.Extensions.Logging;
    using Unity;

    [SupportedOSPlatform("windows")]
    public partial class ClusterRegistryProvider<T> : ICrudStorageProvider<T> where T : IEntity
    {
        private readonly ClusterRegistryStoreSettings storeSettings;
        private readonly ILogger<ClusterRegistryProvider<T>> logger;
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
        private readonly string collectionName;

        private IRegistryProvider? registryProvider;
        private bool disposed;

        public ClusterRegistryProvider(IServiceProvider serviceProvider, string name)
            : base(serviceProvider, name)
        {
            this.storeSettings = this.ConfigReader.ReadSettings<ClusterRegistryStoreSettings>(name);
            this.logger = this.GetLogger<ClusterRegistryProvider<T>>();
            this.collectionName = typeof(T).Name;
            this.InitializeClusterConnection();
        }

        public ClusterRegistryProvider(UnityContainer container, string name)
            : base(container, name)
        {
            this.storeSettings = this.ConfigReader.ReadSettings<ClusterRegistryStoreSettings>(name);
            this.logger = this.GetLogger<ClusterRegistryProvider<T>>();
            this.collectionName = typeof(T).Name;
            this.InitializeClusterConnection();
        }

        public void InitializeClusterConnection()
        {
            try
            {
                // Create registry provider - will automatically fall back to local registry if cluster is not available
                this.registryProvider = RegistryProviderFactory.Create(
                    this.storeSettings.ClusterName,
                    this.storeSettings.RootPath,
                    this.storeSettings.FallbackToLocalRegistry,
                    this.logger);

                this.logger.LogInformation("Initialized {ProviderType} registry provider for entity type {EntityType}",
                    this.registryProvider.IsCluster ? "cluster" : "local", typeof(T).Name);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to initialize registry provider");
                throw;
            }
        }

        private async Task<IRegistryKey> GetOrCreateCollectionKeyAsync(CancellationToken cancellationToken)
        {
            await this.semaphore.WaitAsync(cancellationToken);
            try
            {
                var keyPath = $@"{this.storeSettings.ApplicationName}\{this.storeSettings.ServiceName}\{this.collectionName}";
                return this.registryProvider!.GetOrCreateKey(keyPath);
            }
            finally
            {
                this.semaphore.Release();
            }
        }


        protected string CreateKeyHash(string key)
        {
            // Use SHA256 to create a fixed-length key name that's safe for registry
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
            return Convert.ToBase64String(hashBytes).Replace('/', '_').Replace('+', '-').TrimEnd('=');
        }

        public async Task<T?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();

            try
            {
                using var collectionKey = await this.GetOrCreateCollectionKeyAsync(cancellationToken);
                var hashedKey = this.CreateKeyHash(key);

                // Get the value as binary data
                var data = collectionKey.GetBinaryValue(hashedKey);

                if (data == null)
                {
                    return default(T);
                }

                // Deserialize the data
                return await this.Serializer.DeserializeAsync(data, cancellationToken);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to get entity with key {Key}", key);
                throw;
            }
        }

        public async Task SaveAsync(string key, T entity, CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();

            try
            {
                using var collectionKey = await this.GetOrCreateCollectionKeyAsync(cancellationToken);
                var hashedKey = this.CreateKeyHash(key);

                // Serialize the entity
                var data = await this.Serializer.SerializeAsync(entity, cancellationToken);

                // Check size limit
                if (data.Length > this.storeSettings.MaxValueSizeKB * 1024)
                {
                    throw new InvalidOperationException($"Value size {data.Length} bytes exceeds maximum {this.storeSettings.MaxValueSizeKB}KB");
                }

                // Save to registry as binary
                collectionKey.SetBinaryValue(hashedKey, data);

                this.logger.LogDebug("Saved entity with key {Key} to cluster registry", key);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to save entity with key {Key}", key);
                throw;
            }
        }

        public async Task<IEnumerable<T>> GetAllAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();
            var results = new List<T>();

            try
            {
                using var collectionKey = await this.GetOrCreateCollectionKeyAsync(cancellationToken);

                // Enumerate all value names
                var valueNames = collectionKey.EnumerateValueNames();

                foreach (var valueName in valueNames)
                {
                    try
                    {
                        var data = collectionKey.GetBinaryValue(valueName);
                        if (data != null)
                        {
                            var entity = await this.Serializer.DeserializeAsync(data, cancellationToken);
                            if (entity != null && (predicate == null || predicate.Compile()(entity)))
                            {
                                results.Add(entity);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogWarning(ex, "Failed to deserialize value {ValueName}, skipping", valueName);
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to get all entities");
                throw;
            }
        }

        public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();

            try
            {
                using var collectionKey = await this.GetOrCreateCollectionKeyAsync(cancellationToken);
                var hashedKey = this.CreateKeyHash(key);

                collectionKey.DeleteValue(hashedKey);

                this.logger.LogDebug("Deleted entity with key {Key} from cluster registry", key);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to delete entity with key {Key}", key);
                throw;
            }
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();

            try
            {
                using var collectionKey = await this.GetOrCreateCollectionKeyAsync(cancellationToken);
                var hashedKey = this.CreateKeyHash(key);

                var value = collectionKey.GetStringValue(hashedKey);
                return value != null;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to check existence of key {Key}", key);
                throw;
            }
        }

        public async Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            var entities = await this.GetAllAsync(predicate, cancellationToken);
            return entities.Count();
        }

        public async Task<IEnumerable<T>> GetManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            var results = new List<T>();

            foreach (var key in keys)
            {
                var entity = await this.GetAsync(key, cancellationToken);
                if (entity != null)
                {
                    results.Add(entity);
                }
            }

            return results;
        }

        public async Task SaveManyAsync(IEnumerable<KeyValuePair<string, T>> entities, CancellationToken cancellationToken = default)
        {
            foreach (var (key, entity) in entities)
            {
                await this.SaveAsync(key, entity, cancellationToken);
            }
        }

        public async Task<long> ClearAsync(CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();
            long count = 0;

            try
            {
                using var collectionKey = await this.GetOrCreateCollectionKeyAsync(cancellationToken);

                // Get all value names first
                var valueNames = collectionKey.EnumerateValueNames();
                count = valueNames.Count;

                // Clear all values
                collectionKey.ClearValues();

                this.logger.LogDebug("Cleared {Count} entities from cluster registry", count);
                return count;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to clear entities");
                throw;
            }
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(ClusterRegistryProvider<T>));
            }
        }

        public void Dispose()
        {
            if (!this.disposed)
            {
                this.registryProvider?.Dispose();
                this.semaphore?.Dispose();
                this.disposed = true;
            }
        }
    }
}