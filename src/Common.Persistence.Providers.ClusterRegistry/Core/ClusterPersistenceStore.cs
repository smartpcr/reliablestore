//-------------------------------------------------------------------------------
// <copyright file="ClusterPersistenceStore.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Persistence.Providers.ClusterRegistry.Api;
    using Common.Persistence.Providers.ClusterRegistry.Serialization;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Implementation of cluster registry persistence store.
    /// </summary>
    public sealed class ClusterPersistenceStore : IClusterPersistenceStore
    {
        private readonly ClusterPersistenceConfiguration configuration;
        private readonly IClusterPersistenceSerializer serializer;
        private readonly ILogger<ClusterPersistenceStore> logger;
        private readonly SemaphoreSlim connectionSemaphore;

        private SafeClusterHandle? clusterHandle;
        private SafeClusterKeyHandle? rootKeyHandle;
        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterPersistenceStore"/> class.
        /// </summary>
        /// <param name="configuration">The configuration for the store.</param>
        /// <param name="serializer">The serializer to use for values.</param>
        /// <param name="logger">The logger instance.</param>
        public ClusterPersistenceStore(
            ClusterPersistenceConfiguration configuration,
            IClusterPersistenceSerializer? serializer = null,
            ILogger<ClusterPersistenceStore>? logger = null)
        {
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.configuration.Validate();

            this.serializer = serializer ?? new JsonClusterPersistenceSerializer(this.configuration.EnableCompression);
            this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ClusterPersistenceStore>.Instance;
            this.connectionSemaphore = new SemaphoreSlim(1, 1);

            this.Name = $"{this.configuration.ApplicationName}.{this.configuration.ServiceName}";
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public bool IsConnected => this.clusterHandle != null && !this.clusterHandle.IsInvalid;

        /// <inheritdoc />
        public async Task SetAsync<TKey, TValue>(string collectionName, TKey key, TValue value, CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            this.ThrowIfDisposed();
            ClusterPersistenceStore.ValidateCollectionName(collectionName);
            if (key == null) throw new ArgumentNullException(nameof(key));

            await this.EnsureConnectedAsync(cancellationToken);

            var serializedKey = this.CreateKeyHash(key);
            var serializedValue = this.serializer.Serialize(new KeyValuePair<TKey, TValue>(key, value));

            this.ValidateValueSize(serializedValue);

            await this.ExecuteWithRetryAsync(async () =>
            {
                using var collectionKey = await this.GetOrCreateCollectionKeyAsync(collectionName, cancellationToken);
                collectionKey.SetStringValue(serializedKey, serializedValue);

                this.logger.LogDebug("Set value for key '{Key}' in collection '{Collection}'", key, collectionName);
            }, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<ClusterPersistenceResult<TValue>> GetAsync<TKey, TValue>(string collectionName, TKey key, CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            this.ThrowIfDisposed();
            ClusterPersistenceStore.ValidateCollectionName(collectionName);
            if (key == null) throw new ArgumentNullException(nameof(key));

            await this.EnsureConnectedAsync(cancellationToken);

            var serializedKey = this.CreateKeyHash(key);

            return await this.ExecuteWithRetryAsync(async () =>
            {
                using var collectionKey = await this.GetCollectionKeyAsync(collectionName, cancellationToken);
                if (collectionKey == null)
                {
                    this.logger.LogDebug("Collection '{Collection}' does not exist", collectionName);
                    return ClusterPersistenceResult<TValue>.NoValue();
                }

                var serializedValue = collectionKey.GetStringValue(serializedKey);
                if (serializedValue == null)
                {
                    this.logger.LogDebug("Key '{Key}' not found in collection '{Collection}'", key, collectionName);
                    return ClusterPersistenceResult<TValue>.NoValue();
                }

                var kvp = this.serializer.Deserialize<KeyValuePair<TKey, TValue>>(serializedValue);
                this.logger.LogDebug("Retrieved value for key '{Key}' from collection '{Collection}'", key, collectionName);
                return ClusterPersistenceResult<TValue>.WithValue(kvp.Value);
            }, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<bool> RemoveAsync<TKey>(string collectionName, TKey key, CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            this.ThrowIfDisposed();
            ClusterPersistenceStore.ValidateCollectionName(collectionName);
            if (key == null) throw new ArgumentNullException(nameof(key));

            await this.EnsureConnectedAsync(cancellationToken);

            var serializedKey = this.CreateKeyHash(key);

            return await this.ExecuteWithRetryAsync(async () =>
            {
                using var collectionKey = await this.GetCollectionKeyAsync(collectionName, cancellationToken);
                if (collectionKey == null)
                {
                    this.logger.LogDebug("Collection '{Collection}' does not exist", collectionName);
                    return false;
                }

                var removed = collectionKey.DeleteValue(serializedKey);
                if (removed)
                {
                    this.logger.LogDebug("Removed key '{Key}' from collection '{Collection}'", key, collectionName);
                }
                else
                {
                    this.logger.LogDebug("Key '{Key}' not found in collection '{Collection}'", key, collectionName);
                }
                return removed;
            }, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<bool> ContainsKeyAsync<TKey>(string collectionName, TKey key, CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            this.ThrowIfDisposed();
            ClusterPersistenceStore.ValidateCollectionName(collectionName);
            if (key == null) throw new ArgumentNullException(nameof(key));

            await this.EnsureConnectedAsync(cancellationToken);

            var serializedKey = this.CreateKeyHash(key);

            return await this.ExecuteWithRetryAsync(async () =>
            {
                using var collectionKey = await this.GetCollectionKeyAsync(collectionName, cancellationToken);
                if (collectionKey == null)
                {
                    return false;
                }

                var value = collectionKey.GetStringValue(serializedKey);
                return value != null;
            }, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyDictionary<TKey, TValue>> GetAllAsync<TKey, TValue>(string collectionName, CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            this.ThrowIfDisposed();
            ClusterPersistenceStore.ValidateCollectionName(collectionName);

            await this.EnsureConnectedAsync(cancellationToken);

            return await this.ExecuteWithRetryAsync(async () =>
            {
                using var collectionKey = await this.GetCollectionKeyAsync(collectionName, cancellationToken);
                if (collectionKey == null)
                {
                    this.logger.LogDebug("Collection '{Collection}' does not exist", collectionName);
                    return new Dictionary<TKey, TValue>();
                }

                var result = new Dictionary<TKey, TValue>();
                var valueNames = collectionKey.EnumerateValueNames();

                foreach (var valueName in valueNames)
                {
                    try
                    {
                        var serializedValue = collectionKey.GetStringValue(valueName);
                        if (serializedValue != null)
                        {
                            var kvp = this.serializer.Deserialize<KeyValuePair<TKey, TValue>>(serializedValue);
                            result[kvp.Key] = kvp.Value;
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogWarning(ex, "Failed to deserialize value '{ValueName}' in collection '{Collection}'", valueName, collectionName);
                        // Continue with other values
                    }
                }

                this.logger.LogDebug("Retrieved {Count} items from collection '{Collection}'", result.Count, collectionName);
                return (IReadOnlyDictionary<TKey, TValue>)result;
            }, cancellationToken);
        }

        /// <inheritdoc />
        public async Task ClearCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();
            ClusterPersistenceStore.ValidateCollectionName(collectionName);

            await this.EnsureConnectedAsync(cancellationToken);

            await this.ExecuteWithRetryAsync(async () =>
            {
                using var collectionKey = await this.GetCollectionKeyAsync(collectionName, cancellationToken);
                if (collectionKey != null)
                {
                    collectionKey.ClearValues();
                    this.logger.LogDebug("Cleared all values from collection '{Collection}'", collectionName);
                }
            }, cancellationToken);
        }

        /// <inheritdoc />
        public async Task ExecuteTransactionAsync(IEnumerable<IClusterPersistenceOperation> operations, CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();
            if (operations == null) throw new ArgumentNullException(nameof(operations));

            var operationList = operations.ToList();
            if (operationList.Count == 0)
            {
                return; // Nothing to do
            }

            await this.EnsureConnectedAsync(cancellationToken);

            await this.ExecuteWithRetryAsync(async () =>
            {
                using var batchHandle = SafeClusterBatchHandle.Create(this.rootKeyHandle!);

                foreach (var operation in operationList)
                {
                    var collectionPath = this.configuration.GetCollectionPath(operation.CollectionName);

                    switch (operation.OperationType)
                    {
                        case ClusterPersistenceOperationType.Set:
                            if (operation.SerializedValue == null)
                            {
                                throw new ClusterPersistenceException($"Set operation requires a serialized value. Operation: {operation.CollectionName}\\{operation.Key}");
                            }
                            this.ValidateValueSize(operation.SerializedValue);
                            batchHandle.AddSetValueCommand(collectionPath, operation.Key, operation.SerializedValue);
                            break;

                        case ClusterPersistenceOperationType.Remove:
                            batchHandle.AddDeleteValueCommand(collectionPath, operation.Key);
                            break;

                        case ClusterPersistenceOperationType.Clear:
                            // For clear operations, we need to enumerate and delete individual values
                            // This is a limitation of the batch API
                            using (var collectionKey = await this.GetCollectionKeyAsync(operation.CollectionName, cancellationToken))
                            {
                                if (collectionKey != null)
                                {
                                    var valueNames = collectionKey.EnumerateValueNames();
                                    foreach (var valueName in valueNames)
                                    {
                                        batchHandle.AddDeleteValueCommand(collectionPath, valueName);
                                    }
                                }
                            }
                            break;

                        default:
                            throw new ClusterPersistenceException($"Unsupported operation type: {operation.OperationType}");
                    }
                }

                batchHandle.Commit();
                this.logger.LogDebug("Executed transaction with {Count} operations", operationList.Count);
            }, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> GetCollectionNamesAsync(CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();

            await this.EnsureConnectedAsync(cancellationToken);

            return await this.ExecuteWithRetryAsync(() =>
            {
                var basePath = this.configuration.GetFullPath();
                using var baseKey = this.rootKeyHandle!.OpenSubKey(basePath);

                if (baseKey == null)
                {
                    this.logger.LogDebug("Base path '{BasePath}' does not exist", basePath);
                    return Task.FromResult((IReadOnlyList<string>)Array.Empty<string>().ToList());
                }

                var collectionNames = baseKey.EnumerateSubKeyNames();
                this.logger.LogDebug("Found {Count} collections", collectionNames.Count);
                return Task.FromResult((IReadOnlyList<string>)collectionNames.ToList());
            }, cancellationToken);
        }

        /// <summary>
        /// Ensures the store is connected to the cluster.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            if (this.IsConnected)
            {
                return;
            }

            await this.connectionSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (this.IsConnected)
                {
                    return; // Another thread already connected
                }

                this.logger.LogDebug("Connecting to cluster '{ClusterName}'", this.configuration.ClusterName ?? "local");

                this.clusterHandle = SafeClusterHandle.Open(this.configuration.ClusterName);
                this.rootKeyHandle = this.clusterHandle.GetRootKey();

                this.logger.LogInformation("Connected to cluster '{ClusterName}'", this.configuration.ClusterName ?? "local");
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to connect to cluster '{ClusterName}'", this.configuration.ClusterName ?? "local");
                throw;
            }
            finally
            {
                this.connectionSemaphore.Release();
            }
        }

        /// <summary>
        /// Gets or creates a collection key.
        /// </summary>
        /// <param name="collectionName">The collection name.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A safe cluster key handle for the collection.</returns>
        private Task<SafeClusterKeyHandle> GetOrCreateCollectionKeyAsync(string collectionName, CancellationToken cancellationToken)
        {
            var collectionPath = this.configuration.GetCollectionPath(collectionName);
            return Task.FromResult(this.rootKeyHandle!.CreateOrOpenSubKey(collectionPath));
        }

        /// <summary>
        /// Gets an existing collection key.
        /// </summary>
        /// <param name="collectionName">The collection name.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A safe cluster key handle for the collection, or null if it doesn't exist.</returns>
        private Task<SafeClusterKeyHandle?> GetCollectionKeyAsync(string collectionName, CancellationToken cancellationToken)
        {
            var collectionPath = this.configuration.GetCollectionPath(collectionName);
            return Task.FromResult(this.rootKeyHandle!.OpenSubKey(collectionPath));
        }

        /// <summary>
        /// Executes an operation with retry logic.
        /// </summary>
        /// <typeparam name="T">The return type.</typeparam>
        /// <param name="operation">The operation to execute.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The result of the operation.</returns>
        private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
        {
            var attempt = 0;
            while (true)
            {
                try
                {
                    return await operation();
                }
                catch (Exception ex) when (attempt < this.configuration.RetryAttempts && ClusterPersistenceStore.ShouldRetry(ex))
                {
                    attempt++;
                    var delay = this.configuration.UseExponentialBackoff
                        ? TimeSpan.FromMilliseconds(this.configuration.RetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1))
                        : this.configuration.RetryDelay;

                    this.logger.LogWarning(ex, "Operation failed (attempt {Attempt}/{MaxAttempts}), retrying in {Delay}ms",
                        attempt, this.configuration.RetryAttempts, delay.TotalMilliseconds);

                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Executes an operation with retry logic.
        /// </summary>
        /// <param name="operation">The operation to execute.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        private async Task ExecuteWithRetryAsync(Func<Task> operation, CancellationToken cancellationToken)
        {
            await this.ExecuteWithRetryAsync(async () =>
            {
                await operation();
                return true; // Dummy return value
            }, cancellationToken);
        }

        /// <summary>
        /// Determines if an exception should trigger a retry.
        /// </summary>
        /// <param name="exception">The exception to check.</param>
        /// <returns>True if the operation should be retried.</returns>
        private static bool ShouldRetry(Exception exception)
        {
            return exception is ClusterConnectionException ||
                   exception is ClusterTransactionException ||
                   (exception is ClusterPersistenceException && !(exception is ClusterAccessDeniedException));
        }

        /// <summary>
        /// Creates a hash of the key for use as a registry value name.
        /// </summary>
        /// <typeparam name="TKey">The type of the key.</typeparam>
        /// <param name="key">The key to hash.</param>
        /// <returns>A hash string that can be used as a registry value name.</returns>
        private string CreateKeyHash<TKey>(TKey key)
        {
            var serializedKey = this.serializer.Serialize(key);
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(serializedKey));
#if NET5_0_OR_GREATER
            return Convert.ToHexString(hashBytes);
#else
            return BitConverter.ToString(hashBytes).Replace("-", string.Empty);
#endif
        }

        /// <summary>
        /// Validates that a collection name is valid.
        /// </summary>
        /// <param name="collectionName">The collection name to validate.</param>
        private static void ValidateCollectionName(string collectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
            {
                throw new ArgumentException("Collection name cannot be null or whitespace.", nameof(collectionName));
            }

            if (collectionName.Contains('\\') || collectionName.Contains('/'))
            {
                throw new ArgumentException("Collection name cannot contain path separators.", nameof(collectionName));
            }
        }

        /// <summary>
        /// Validates that a serialized value is within size limits.
        /// </summary>
        /// <param name="serializedValue">The serialized value to validate.</param>
        private void ValidateValueSize(string serializedValue)
        {
            var sizeInBytes = Encoding.UTF8.GetByteCount(serializedValue);
            if (sizeInBytes > this.configuration.MaxValueSize)
            {
                throw new ClusterPersistenceException($"Serialized value size ({sizeInBytes} bytes) exceeds maximum allowed size ({this.configuration.MaxValueSize} bytes).");
            }
        }

        /// <summary>
        /// Throws an exception if the store has been disposed.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(ClusterPersistenceStore));
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.rootKeyHandle?.Dispose();
            this.clusterHandle?.Dispose();
            this.connectionSemaphore?.Dispose();

            this.disposed = true;

            this.logger.LogDebug("Disposed cluster persistence store '{Name}'", this.Name);
        }
    }
}