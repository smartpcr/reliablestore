//-------------------------------------------------------------------------------
// <copyright file="TransactionalResource.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Transaction
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Persistence.Contract;
    using Common.Tx;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Generic transactional resource that wraps a storage provider to enable transaction support.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    public class TransactionalResource<T> : ITransactionalResource where T : class, IEntity
    {
        private readonly ICrudStorageProvider<T> storageProvider;
        private readonly ILogger<TransactionalResource<T>> logger;
        private readonly Dictionary<string, TransactionOperation<T>> stagedOperations;
        private readonly HashSet<string> deletedKeys;
        private readonly Dictionary<string, TransactionSnapshot<T>> savepointSnapshots;
        private readonly object operationsLock = new();
        private readonly string resourceId;

        /// <summary>
        /// Initializes a new instance of the <see cref="TransactionalResource{T}"/> class.
        /// </summary>
        /// <param name="storageProvider">The underlying storage provider.</param>
        /// <param name="logger">Logger instance.</param>
        public TransactionalResource(
            ICrudStorageProvider<T> storageProvider,
            ILogger<TransactionalResource<T>>? logger = null)
        {
            this.storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
            this.logger = logger ?? CreateNullLogger();
            this.stagedOperations = new Dictionary<string, TransactionOperation<T>>();
            this.deletedKeys = new HashSet<string>();
            this.savepointSnapshots = new Dictionary<string, TransactionSnapshot<T>>();
            this.resourceId = $"TransactionalResource_{typeof(T).Name}_{Guid.NewGuid():N}";
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TransactionalResource{T}"/> class with a single entity.
        /// </summary>
        /// <param name="entity">The entity to operate on.</param>
        /// <param name="keySelector">Function to extract the key from the entity.</param>
        /// <param name="storageProvider">The underlying storage provider.</param>
        /// <param name="logger">Logger instance.</param>
        public TransactionalResource(
            T entity,
            Func<T, string> keySelector,
            ICrudStorageProvider<T> storageProvider,
            ILogger<TransactionalResource<T>>? logger = null)
            : this(storageProvider, logger)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

            var key = keySelector(entity);
            this.StageOperation(key, TransactionOperationType.Save, entity);
        }

        /// <summary>
        /// Gets the unique identifier for this transactional resource.
        /// </summary>
        public string ResourceId => this.resourceId;

        /// <summary>
        /// Stages a save operation for the specified entity.
        /// </summary>
        /// <param name="key">The entity key.</param>
        /// <param name="entity">The entity to save.</param>
        public void SaveEntity(string key, T entity)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty.", nameof(key));
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            this.StageOperation(key, TransactionOperationType.Save, entity);
        }

        /// <summary>
        /// Stages a delete operation for the specified key.
        /// </summary>
        /// <param name="key">The entity key to delete.</param>
        public void DeleteEntity(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty.", nameof(key));

            this.StageOperation(key, TransactionOperationType.Delete, null);
        }

        /// <summary>
        /// Stages multiple save operations.
        /// </summary>
        /// <param name="entities">The entities to save with their keys.</param>
        public void SaveEntities(IEnumerable<KeyValuePair<string, T>> entities)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));

            foreach (var (key, entity) in entities)
            {
                this.SaveEntity(key, entity);
            }
        }

        /// <summary>
        /// Gets the current state of an entity, considering staged operations.
        /// </summary>
        /// <param name="key">The entity key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The entity if found, null otherwise.</returns>
        public async Task<T?> GetEntityAsync(string key, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty.", nameof(key));

            lock (this.operationsLock)
            {
                // Check if key was deleted in this transaction
                if (this.deletedKeys.Contains(key))
                    return null;

                // Check staged operations first
                if (this.stagedOperations.TryGetValue(key, out var operation) && 
                    operation.Type == TransactionOperationType.Save)
                {
                    return operation.Entity;
                }
            }

            // Fall back to storage provider
            return await this.storageProvider.GetAsync(key, cancellationToken);
        }

        /// <summary>
        /// Gets all staged operations for inspection.
        /// </summary>
        /// <returns>A copy of the staged operations.</returns>
        public IReadOnlyDictionary<string, TransactionOperation<T>> GetStagedOperations()
        {
            lock (this.operationsLock)
            {
                return new Dictionary<string, TransactionOperation<T>>(this.stagedOperations);
            }
        }

        /// <summary>
        /// Prepares the transaction by validating all staged operations.
        /// </summary>
        /// <param name="transaction">The transaction context.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the resource can commit, false otherwise.</returns>
        public async Task<bool> PrepareAsync(ITransaction transaction, CancellationToken cancellationToken = default)
        {
            try
            {
                this.logger.LogDebug("Preparing transaction for resource {ResourceId} with {OperationCount} operations", 
                    this.ResourceId, this.stagedOperations.Count + this.deletedKeys.Count);

                // Validate all staged operations
                Dictionary<string, TransactionOperation<T>> operations;
                HashSet<string> deletions;

                lock (this.operationsLock)
                {
                    operations = new Dictionary<string, TransactionOperation<T>>(this.stagedOperations);
                    deletions = new HashSet<string>(this.deletedKeys);
                }

                // Validate save operations
                foreach (var (key, operation) in operations)
                {
                    if (operation.Type == TransactionOperationType.Save && operation.Entity != null)
                    {
                        if (!this.ValidateEntity(operation.Entity))
                        {
                            this.logger.LogWarning("Entity validation failed for key {Key}", key);
                            return false;
                        }
                    }
                }

                // Validate delete operations - check if entities exist
                foreach (var key in deletions)
                {
                    if (!await this.storageProvider.ExistsAsync(key, cancellationToken))
                    {
                        this.logger.LogWarning("Cannot delete non-existent entity with key {Key}", key);
                        return false;
                    }
                }

                // Check if underlying storage provider supports transactions
                if (this.storageProvider is ITransactionalResource transactionalProvider)
                {
                    var result = await transactionalProvider.PrepareAsync(transaction, cancellationToken);
                    if (!result)
                    {
                        this.logger.LogWarning("Underlying storage provider failed to prepare");
                        return false;
                    }
                }

                this.logger.LogDebug("Successfully prepared transaction for resource {ResourceId}", this.ResourceId);
                return true;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to prepare transaction for resource {ResourceId}", this.ResourceId);
                return false;
            }
        }

        /// <summary>
        /// Commits all staged operations to the underlying storage provider.
        /// </summary>
        /// <param name="transaction">The transaction context.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task CommitAsync(ITransaction transaction, CancellationToken cancellationToken = default)
        {
            try
            {
                this.logger.LogDebug("Committing transaction for resource {ResourceId}", this.ResourceId);

                Dictionary<string, TransactionOperation<T>> operations;
                HashSet<string> deletions;

                lock (this.operationsLock)
                {
                    operations = new Dictionary<string, TransactionOperation<T>>(this.stagedOperations);
                    deletions = new HashSet<string>(this.deletedKeys);
                }

                // Apply deletions first
                foreach (var key in deletions)
                {
                    await this.storageProvider.DeleteAsync(key, cancellationToken);
                    this.logger.LogDebug("Deleted entity with key {Key}", key);
                }

                // Apply save operations
                var saveOperations = operations
                    .Where(kvp => kvp.Value.Type == TransactionOperationType.Save && kvp.Value.Entity != null)
                    .Select(kvp => new KeyValuePair<string, T>(kvp.Key, kvp.Value.Entity!))
                    .ToList();

                if (saveOperations.Count > 0)
                {
                    await this.storageProvider.SaveManyAsync(saveOperations, cancellationToken);
                    this.logger.LogDebug("Saved {Count} entities", saveOperations.Count);
                }

                // Commit underlying transactional provider if applicable
                if (this.storageProvider is ITransactionalResource transactionalProvider)
                {
                    await transactionalProvider.CommitAsync(transaction, cancellationToken);
                }

                // Clear staged operations after successful commit
                lock (this.operationsLock)
                {
                    this.stagedOperations.Clear();
                    this.deletedKeys.Clear();
                }

                this.logger.LogInformation("Successfully committed transaction for resource {ResourceId}", this.ResourceId);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to commit transaction for resource {ResourceId}", this.ResourceId);
                throw;
            }
        }

        /// <summary>
        /// Rolls back all staged operations.
        /// </summary>
        /// <param name="transaction">The transaction context.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task RollbackAsync(ITransaction transaction, CancellationToken cancellationToken = default)
        {
            try
            {
                this.logger.LogDebug("Rolling back transaction for resource {ResourceId}", this.ResourceId);

                // Rollback underlying transactional provider if applicable
                if (this.storageProvider is ITransactionalResource transactionalProvider)
                {
                    await transactionalProvider.RollbackAsync(transaction, cancellationToken);
                }

                // Clear all staged operations
                lock (this.operationsLock)
                {
                    this.stagedOperations.Clear();
                    this.deletedKeys.Clear();
                }

                this.logger.LogInformation("Successfully rolled back transaction for resource {ResourceId}", this.ResourceId);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to rollback transaction for resource {ResourceId}", this.ResourceId);
                throw;
            }
        }

        /// <summary>
        /// Creates a savepoint for nested transactions.
        /// </summary>
        /// <param name="transaction">The transaction context.</param>
        /// <param name="savepoint">The savepoint to create.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task CreateSavepointAsync(ITransaction transaction, ISavepoint savepoint, CancellationToken cancellationToken = default)
        {
            this.logger.LogDebug("Creating savepoint {SavepointName} for resource {ResourceId}", 
                savepoint.Name, this.ResourceId);

            // Create a snapshot of current staged operations
            lock (this.operationsLock)
            {
                var snapshot = new TransactionSnapshot<T>
                {
                    StagedOperations = new Dictionary<string, TransactionOperation<T>>(this.stagedOperations),
                    DeletedKeys = new HashSet<string>(this.deletedKeys)
                };

                // Store the snapshot using savepoint name as key
                this.savepointSnapshots[savepoint.Name] = snapshot;
            }

            // Delegate to underlying provider if it supports savepoints
            if (this.storageProvider is ITransactionalResource transactionalProvider)
            {
                await transactionalProvider.CreateSavepointAsync(transaction, savepoint, cancellationToken);
            }
        }

        /// <summary>
        /// Rolls back to a previous savepoint.
        /// </summary>
        /// <param name="transaction">The transaction context.</param>
        /// <param name="savepoint">The savepoint to rollback to.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task RollbackToSavepointAsync(ITransaction transaction, ISavepoint savepoint, CancellationToken cancellationToken = default)
        {
            this.logger.LogDebug("Rolling back to savepoint {SavepointName} for resource {ResourceId}", 
                savepoint.Name, this.ResourceId);

            // Restore from snapshot
            if (this.savepointSnapshots.TryGetValue(savepoint.Name, out var snapshot))
            {
                lock (this.operationsLock)
                {
                    this.stagedOperations.Clear();
                    this.deletedKeys.Clear();
                    
                    foreach (var (key, operation) in snapshot.StagedOperations)
                    {
                        this.stagedOperations[key] = operation;
                    }
                    
                    foreach (var key in snapshot.DeletedKeys)
                    {
                        this.deletedKeys.Add(key);
                    }
                }
            }

            // Delegate to underlying provider if it supports savepoints
            if (this.storageProvider is ITransactionalResource transactionalProvider)
            {
                await transactionalProvider.RollbackToSavepointAsync(transaction, savepoint, cancellationToken);
            }
        }

        /// <summary>
        /// Discards savepoint data.
        /// </summary>
        /// <param name="transaction">The transaction context.</param>
        /// <param name="savepointToDiscard">The savepoint to discard.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task DiscardSavepointDataAsync(ITransaction transaction, ISavepoint savepointToDiscard, CancellationToken cancellationToken = default)
        {
            this.logger.LogDebug("Discarding savepoint {SavepointName} for resource {ResourceId}", 
                savepointToDiscard.Name, this.ResourceId);

            // Remove snapshot data
            this.savepointSnapshots.Remove(savepointToDiscard.Name);

            // Delegate to underlying provider if it supports savepoints
            if (this.storageProvider is ITransactionalResource transactionalProvider)
            {
                await transactionalProvider.DiscardSavepointDataAsync(transaction, savepointToDiscard, cancellationToken);
            }
        }

        private void StageOperation(string key, TransactionOperationType type, T? entity)
        {
            lock (this.operationsLock)
            {
                if (type == TransactionOperationType.Delete)
                {
                    this.deletedKeys.Add(key);
                    this.stagedOperations.Remove(key); // Remove any pending save operation
                }
                else if (type == TransactionOperationType.Save)
                {
                    this.stagedOperations[key] = new TransactionOperation<T>
                    {
                        Type = type,
                        Entity = entity,
                        Timestamp = DateTime.UtcNow
                    };
                    this.deletedKeys.Remove(key); // Remove from deleted if it was marked for deletion
                }
            }

            this.logger.LogDebug("Staged {OperationType} operation for key {Key}", type, key);
        }

        private bool ValidateEntity(T entity)
        {
            // Basic validation - can be extended with more sophisticated validation
            if (entity == null) return false;
            if (string.IsNullOrEmpty(entity.Key)) return false;
            if (entity.Version <= 0) return false;

            return true;
        }

        private static ILogger<TransactionalResource<T>> CreateNullLogger()
        {
            return new Microsoft.Extensions.Logging.Abstractions.NullLogger<TransactionalResource<T>>();
        }
    }

    /// <summary>
    /// Represents a staged transaction operation.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    public class TransactionOperation<T> where T : class, IEntity
    {
        /// <summary>
        /// Gets or sets the type of operation.
        /// </summary>
        public TransactionOperationType Type { get; set; }

        /// <summary>
        /// Gets or sets the entity (null for delete operations).
        /// </summary>
        public T? Entity { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when the operation was staged.
        /// </summary>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Represents the type of transaction operation.
    /// </summary>
    public enum TransactionOperationType
    {
        /// <summary>
        /// Save operation.
        /// </summary>
        Save,

        /// <summary>
        /// Delete operation.
        /// </summary>
        Delete
    }

    /// <summary>
    /// Snapshot of transactional resource state for savepoint support.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    internal class TransactionSnapshot<T> where T : class, IEntity
    {
        /// <summary>
        /// Gets or sets the staged operations at the time of snapshot.
        /// </summary>
        public Dictionary<string, TransactionOperation<T>> StagedOperations { get; set; } = new();

        /// <summary>
        /// Gets or sets the deleted keys at the time of snapshot.
        /// </summary>
        public HashSet<string> DeletedKeys { get; set; } = new();
    }
}