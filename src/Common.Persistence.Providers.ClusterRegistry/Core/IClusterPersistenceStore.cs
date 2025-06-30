//-------------------------------------------------------------------------------
// <copyright file="IClusterPersistenceStore.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Core
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Primary interface for cluster registry persistence operations.
    /// Provides a high-level abstraction over Windows Failover Cluster Registry storage.
    /// </summary>
    public interface IClusterPersistenceStore : IDisposable
    {
        /// <summary>
        /// Gets the name of this persistence store instance.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets a value indicating whether the store is connected to the cluster.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Creates or updates a key-value pair in the specified collection.
        /// </summary>
        /// <typeparam name="TKey">The type of the key.</typeparam>
        /// <typeparam name="TValue">The type of the value.</typeparam>
        /// <param name="collectionName">The name of the collection (registry subkey).</param>
        /// <param name="key">The key to store.</param>
        /// <param name="value">The value to store.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task SetAsync<TKey, TValue>(string collectionName, TKey key, TValue value, CancellationToken cancellationToken = default)
            where TKey : notnull;

        /// <summary>
        /// Retrieves a value by key from the specified collection.
        /// </summary>
        /// <typeparam name="TKey">The type of the key.</typeparam>
        /// <typeparam name="TValue">The type of the value.</typeparam>
        /// <param name="collectionName">The name of the collection (registry subkey).</param>
        /// <param name="key">The key to retrieve.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task containing the result with the value if found.</returns>
        Task<ClusterPersistenceResult<TValue>> GetAsync<TKey, TValue>(string collectionName, TKey key, CancellationToken cancellationToken = default)
            where TKey : notnull;

        /// <summary>
        /// Removes a key-value pair from the specified collection.
        /// </summary>
        /// <typeparam name="TKey">The type of the key.</typeparam>
        /// <param name="collectionName">The name of the collection (registry subkey).</param>
        /// <param name="key">The key to remove.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task containing true if the key was removed, false if it didn't exist.</returns>
        Task<bool> RemoveAsync<TKey>(string collectionName, TKey key, CancellationToken cancellationToken = default)
            where TKey : notnull;

        /// <summary>
        /// Checks if a key exists in the specified collection.
        /// </summary>
        /// <typeparam name="TKey">The type of the key.</typeparam>
        /// <param name="collectionName">The name of the collection (registry subkey).</param>
        /// <param name="key">The key to check.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task containing true if the key exists, false otherwise.</returns>
        Task<bool> ContainsKeyAsync<TKey>(string collectionName, TKey key, CancellationToken cancellationToken = default)
            where TKey : notnull;

        /// <summary>
        /// Retrieves all key-value pairs from the specified collection.
        /// </summary>
        /// <typeparam name="TKey">The type of the keys.</typeparam>
        /// <typeparam name="TValue">The type of the values.</typeparam>
        /// <param name="collectionName">The name of the collection (registry subkey).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task containing all key-value pairs in the collection.</returns>
        Task<IReadOnlyDictionary<TKey, TValue>> GetAllAsync<TKey, TValue>(string collectionName, CancellationToken cancellationToken = default)
            where TKey : notnull;

        /// <summary>
        /// Clears all data from the specified collection.
        /// </summary>
        /// <param name="collectionName">The name of the collection (registry subkey).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task ClearCollectionAsync(string collectionName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes multiple operations atomically within a transaction.
        /// </summary>
        /// <param name="operations">The operations to execute.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task ExecuteTransactionAsync(IEnumerable<IClusterPersistenceOperation> operations, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the names of all collections in this store.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task containing the collection names.</returns>
        Task<IReadOnlyList<string>> GetCollectionNamesAsync(CancellationToken cancellationToken = default);
    }
}