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
    using System.Runtime.InteropServices;
    using System.Security.AccessControl;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Persistence.Contract;
    using Common.Persistence.Providers.ClusterRegistry.Api;
    using Microsoft.Extensions.Logging;
    using Unity;

    public partial class ClusterRegistryProvider<T> : BaseProvider<T>, ICrudStorageProvider<T>, IDisposable where T : IEntity
    {
        private readonly ClusterRegistryStoreSettings storeSettings;
        private readonly ILogger<ClusterRegistryProvider<T>> logger;
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
        private readonly string collectionName;

        private SafeClusterHandle? clusterHandle;
        private SafeClusterKeyHandle? rootKeyHandle;
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

        private void InitializeClusterConnection()
        {
            try
            {
                // Open cluster connection
                var clusterPtr = ClusterApiInterop.OpenCluster(this.storeSettings.ClusterName);
                if (clusterPtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"Failed to open cluster '{this.storeSettings.ClusterName ?? "local"}'. Error: {Marshal.GetLastWin32Error()}");
                }

                this.clusterHandle = new SafeClusterHandle(clusterPtr);

                // Get root registry key
                var rootKeyPtr = ClusterApiInterop.GetClusterKey(clusterPtr, RegistryRights.FullControl);
                if (rootKeyPtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"Failed to get cluster registry root key. Error: {Marshal.GetLastWin32Error()}");
                }

                this.rootKeyHandle = new SafeClusterKeyHandle(rootKeyPtr);

                this.logger.LogInformation("Connected to cluster '{ClusterName}' for entity type {EntityType}",
                    this.storeSettings.ClusterName ?? "local", typeof(T).Name);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to initialize cluster connection");
                throw;
            }
        }

        private async Task<SafeClusterKeyHandle> GetOrCreateCollectionKeyAsync(CancellationToken cancellationToken)
        {
            await this.semaphore.WaitAsync(cancellationToken);
            try
            {
                var keyPath = $@"{this.storeSettings.RootPath}\{this.storeSettings.ApplicationName}\{this.storeSettings.ServiceName}\{this.collectionName}";
                return this.OpenOrCreateKey(this.rootKeyHandle!, keyPath);
            }
            finally
            {
                this.semaphore.Release();
            }
        }

        private SafeClusterKeyHandle OpenOrCreateKey(SafeClusterKeyHandle parentKey, string keyPath)
        {
            var pathParts = keyPath.Split('\\');
            var currentKey = parentKey;

            foreach (var part in pathParts)
            {
                if (string.IsNullOrEmpty(part)) continue;

                var result = ClusterApiInterop.ClusterRegOpenKey(currentKey.DangerousGetHandle(), part, RegistryRights.FullControl, out IntPtr subKeyPtr);

                if (result != ClusterApiInterop.ERROR_SUCCESS)
                {
                    // Key doesn't exist, create it
                    int disposition;
                    result = ClusterApiInterop.ClusterRegCreateKey(
                        currentKey.DangerousGetHandle(),
                        part,
                        0,
                        RegistryRights.FullControl,
                        IntPtr.Zero,
                        out subKeyPtr,
                        out disposition);

                    if (result != ClusterApiInterop.ERROR_SUCCESS)
                    {
                        throw new InvalidOperationException($"Failed to create registry key '{part}'. Error: {result}");
                    }
                }

                if (currentKey != parentKey)
                {
                    currentKey.Dispose();
                }

                currentKey = new SafeClusterKeyHandle(subKeyPtr);
            }

            return currentKey;
        }

        private string CreateKeyHash(string key)
        {
            // Use SHA256 to create a fixed-length key name that's safe for registry
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
                return Convert.ToBase64String(hashBytes).Replace('/', '_').Replace('+', '-').TrimEnd('=');
            }
        }

        public async Task<T?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();

            try
            {
                using var collectionKey = await this.GetOrCreateCollectionKeyAsync(cancellationToken);
                var hashedKey = this.CreateKeyHash(key);

                // First get the size
                var dataSize = 0;
                ClusterRegistryValueType valueType;
                var result = ClusterApiInterop.ClusterRegQueryValue(
                    collectionKey,
                    hashedKey,
                    out valueType,
                    IntPtr.Zero,
                    ref dataSize);

                if (result == ClusterErrorCode.FileNotFound)
                {
                    return default(T);
                }

                if (result != ClusterApiInterop.ERROR_MORE_DATA && result != ClusterApiInterop.ERROR_SUCCESS)
                {
                    throw new InvalidOperationException($"Failed to query value size. Error: {result}");
                }

                // Now get the actual data
                var data = Marshal.AllocHGlobal(dataSize);
                result = ClusterApiInterop.ClusterRegQueryValue(
                    collectionKey,
                    hashedKey,
                    out valueType,
                    data,
                    ref dataSize);

                if (result != ClusterApiInterop.ERROR_SUCCESS)
                {
                    throw new InvalidOperationException($"Failed to read value. Error: {result}");
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

                // Save to registry
                var result = ClusterApiInterop.ClusterRegSetValue(
                    collectionKey.DangerousGetHandle(),
                    hashedKey,
                    ClusterRegistryValueType.Binary,
                    data,
                    data.Length);

                if (result != ClusterApiInterop.ERROR_SUCCESS)
                {
                    throw new InvalidOperationException($"Failed to save value. Error: {result}");
                }

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

                int index = 0;
                while (true)
                {
                    var valueName = new StringBuilder(256);
                    int valueNameSize = valueName.Capacity;
                    ClusterRegistryValueType valueType;
                    int dataSize = 0;

                    // First enumerate to get value name and size
                    var result = ClusterApiInterop.ClusterRegEnumValue(
                        collectionKey.DangerousGetHandle(),
                        index,
                        valueName,
                        ref valueNameSize,
                        out valueType,
                        null,
                        ref dataSize);

                    if (result == ClusterApiInterop.ERROR_NO_MORE_ITEMS)
                    {
                        break;
                    }

                    if (result != ClusterApiInterop.ERROR_SUCCESS && result != ClusterApiInterop.ERROR_MORE_DATA)
                    {
                        throw new InvalidOperationException($"Failed to enumerate values. Error: {result}");
                    }

                    // Get the actual data
                    var data = new byte[dataSize];
                    valueNameSize = valueName.Capacity;
                    result = ClusterApiInterop.ClusterRegEnumValue(
                        collectionKey.DangerousGetHandle(),
                        index,
                        valueName,
                        ref valueNameSize,
                        out valueType,
                        data,
                        ref dataSize);

                    if (result == ClusterApiInterop.ERROR_SUCCESS)
                    {
                        var entity = await this.Serializer.DeserializeAsync(data, cancellationToken);
                        if (entity != null && (predicate == null || predicate.Compile()(entity)))
                        {
                            results.Add(entity);
                        }
                    }

                    index++;
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

                var result = ClusterApiInterop.ClusterRegDeleteValue(collectionKey.DangerousGetHandle(), hashedKey);

                if (result != ClusterApiInterop.ERROR_SUCCESS && result != ClusterApiInterop.ERROR_FILE_NOT_FOUND)
                {
                    throw new InvalidOperationException($"Failed to delete value. Error: {result}");
                }

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

                int dataSize = 0;
                ClusterRegistryValueType valueType;
                var result = ClusterApiInterop.ClusterRegQueryValue(
                    collectionKey.DangerousGetHandle(),
                    hashedKey,
                    out valueType,
                    null,
                    ref dataSize);

                return result == ClusterApiInterop.ERROR_SUCCESS || result == ClusterApiInterop.ERROR_MORE_DATA;
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
                var valueNames = new List<string>();
                int index = 0;

                while (true)
                {
                    var valueName = new StringBuilder(256);
                    int valueNameSize = valueName.Capacity;
                    ClusterRegistryValueType valueType;
                    int dataSize = 0;

                    var result = ClusterApiInterop.ClusterRegEnumValue(
                        collectionKey.DangerousGetHandle(),
                        index,
                        valueName,
                        ref valueNameSize,
                        out valueType,
                        null,
                        ref dataSize);

                    if (result == ClusterApiInterop.ERROR_NO_MORE_ITEMS)
                    {
                        break;
                    }

                    if (result == ClusterApiInterop.ERROR_SUCCESS || result == ClusterApiInterop.ERROR_MORE_DATA)
                    {
                        valueNames.Add(valueName.ToString());
                    }

                    index++;
                }

                // Delete all values
                foreach (var valueName in valueNames)
                {
                    var result = ClusterApiInterop.ClusterRegDeleteValue(collectionKey.DangerousGetHandle(), valueName);
                    if (result == ClusterApiInterop.ERROR_SUCCESS)
                    {
                        count++;
                    }
                }

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
                this.rootKeyHandle?.Dispose();
                this.clusterHandle?.Dispose();
                this.semaphore?.Dispose();
                this.disposed = true;
            }
        }
    }
}