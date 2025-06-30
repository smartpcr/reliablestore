//-------------------------------------------------------------------------------
// <copyright file="IClusterPersistenceOperation.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Core
{
    using System;

    /// <summary>
    /// Represents an operation that can be executed within a cluster persistence transaction.
    /// </summary>
    public interface IClusterPersistenceOperation
    {
        /// <summary>
        /// Gets the type of operation.
        /// </summary>
        ClusterPersistenceOperationType OperationType { get; }

        /// <summary>
        /// Gets the collection name this operation targets.
        /// </summary>
        string CollectionName { get; }

        /// <summary>
        /// Gets the key for this operation.
        /// </summary>
        string Key { get; }

        /// <summary>
        /// Gets the serialized value for set operations, or null for delete operations.
        /// </summary>
        string? SerializedValue { get; }
    }

    /// <summary>
    /// Defines the types of operations that can be performed in a transaction.
    /// </summary>
    public enum ClusterPersistenceOperationType
    {
        /// <summary>
        /// Set or update a key-value pair.
        /// </summary>
        Set,

        /// <summary>
        /// Remove a key-value pair.
        /// </summary>
        Remove,

        /// <summary>
        /// Clear all values in a collection.
        /// </summary>
        Clear
    }

    /// <summary>
    /// Implementation of a cluster persistence operation.
    /// </summary>
    public sealed class ClusterPersistenceOperation : IClusterPersistenceOperation
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterPersistenceOperation"/> class.
        /// </summary>
        /// <param name="operationType">The type of operation.</param>
        /// <param name="collectionName">The collection name.</param>
        /// <param name="key">The key.</param>
        /// <param name="serializedValue">The serialized value (null for delete operations).</param>
        public ClusterPersistenceOperation(
            ClusterPersistenceOperationType operationType,
            string collectionName,
            string key,
            string? serializedValue = null)
        {
            this.OperationType = operationType;
            this.CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
            this.Key = key ?? throw new ArgumentNullException(nameof(key));
            this.SerializedValue = serializedValue;
        }

        /// <inheritdoc />
        public ClusterPersistenceOperationType OperationType { get; }

        /// <inheritdoc />
        public string CollectionName { get; }

        /// <inheritdoc />
        public string Key { get; }

        /// <inheritdoc />
        public string? SerializedValue { get; }

        /// <summary>
        /// Creates a set operation.
        /// </summary>
        /// <param name="collectionName">The collection name.</param>
        /// <param name="key">The key.</param>
        /// <param name="serializedValue">The serialized value.</param>
        /// <returns>A set operation.</returns>
        public static ClusterPersistenceOperation Set(string collectionName, string key, string serializedValue)
        {
            return new ClusterPersistenceOperation(ClusterPersistenceOperationType.Set, collectionName, key, serializedValue);
        }

        /// <summary>
        /// Creates a remove operation.
        /// </summary>
        /// <param name="collectionName">The collection name.</param>
        /// <param name="key">The key.</param>
        /// <returns>A remove operation.</returns>
        public static ClusterPersistenceOperation Remove(string collectionName, string key)
        {
            return new ClusterPersistenceOperation(ClusterPersistenceOperationType.Remove, collectionName, key);
        }

        /// <summary>
        /// Creates a clear operation.
        /// </summary>
        /// <param name="collectionName">The collection name.</param>
        /// <returns>A clear operation.</returns>
        public static ClusterPersistenceOperation Clear(string collectionName)
        {
            return new ClusterPersistenceOperation(ClusterPersistenceOperationType.Clear, collectionName, string.Empty);
        }
    }
}