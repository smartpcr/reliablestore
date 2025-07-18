//-------------------------------------------------------------------------------
// <copyright file="TransactionalRepository.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Transactional repository implementation that supports rollback
    /// </summary>
    public class TransactionalRepository<T> : ITransactionalResource where T : class
    {
        private readonly IRepository<T> underlyingRepository;
        private readonly ILogger<TransactionalRepository<T>> logger;
        private readonly ConcurrentDictionary<string, TransactionOperation<T>> pendingOperations;
        private readonly ConcurrentDictionary<string, Dictionary<string, TransactionOperation<T>>> savepoints;
        private readonly object operationLock = new object();

        public string ResourceId { get; }

        public TransactionalRepository(IRepository<T> underlyingRepository, ILogger<TransactionalRepository<T>> logger)
        {
            this.underlyingRepository = underlyingRepository ?? throw new ArgumentNullException(nameof(underlyingRepository));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.pendingOperations = new ConcurrentDictionary<string, TransactionOperation<T>>();
            this.savepoints = new ConcurrentDictionary<string, Dictionary<string, TransactionOperation<T>>>();
            this.ResourceId = $"Repository_{typeof(T).Name}_{Guid.NewGuid():N}";
        }

        /// <summary>
        /// Get entity with transaction isolation
        /// </summary>
        public async Task<T> GetAsync(ITransaction transaction, string key, CancellationToken cancellationToken = default)
        {
            // Check for pending operations in this transaction first
            if (this.pendingOperations.TryGetValue(key, out var operation) && operation.TransactionId == transaction.TransactionId)
            {
                switch (operation.Type)
                {
                    case OperationType.Insert:
                    case OperationType.Update:
                        return operation.NewValue;
                    case OperationType.Delete:
                        return null;
                    case OperationType.Read:
                        return operation.OriginalValue;
                }
            }

            // Read from underlying repository
            var entity = await this.underlyingRepository.GetAsync(key, cancellationToken);

            // Track read operation for isolation
            this.TrackOperation(transaction.TransactionId, key, OperationType.Read, entity, entity);

            return entity;
        }

        /// <summary>
        /// Save entity transactionally
        /// </summary>
        public async Task<T> SaveAsync(ITransaction transaction, string key, T entity, CancellationToken cancellationToken = default)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            // Get original value for rollback
            var originalValue = await this.underlyingRepository.GetAsync(key, cancellationToken);
            var operationType = originalValue == null ? OperationType.Insert : OperationType.Update;

            // Track operation for commit/rollback
            this.TrackOperation(transaction.TransactionId, key, operationType, originalValue, entity);

            this.logger.LogDebug("Tracked {OperationType} operation for key {Key} in transaction {TransactionId}",
                operationType, key, transaction.TransactionId);

            return entity;
        }

        /// <summary>
        /// Delete entity transactionally
        /// </summary>
        public async Task<bool> DeleteAsync(ITransaction transaction, string key, CancellationToken cancellationToken = default)
        {
            // Get original value for rollback
            var originalValue = await this.underlyingRepository.GetAsync(key, cancellationToken);
            if (originalValue == null)
            {
                return false; // Nothing to delete
            }

            // Track delete operation
            this.TrackOperation(transaction.TransactionId, key, OperationType.Delete, originalValue, null);

            this.logger.LogDebug("Tracked Delete operation for key {Key} in transaction {TransactionId}",
                key, transaction.TransactionId);

            return true;
        }

        /// <summary>
        /// Check if entity exists with transaction isolation
        /// </summary>
        public async Task<bool> ExistsAsync(ITransaction transaction, string key, CancellationToken cancellationToken = default)
        {
            var entity = await this.GetAsync(transaction, key, cancellationToken);
            return entity != null;
        }

        /// <summary>
        /// Get all entities with transaction isolation
        /// </summary>
        public async Task<IEnumerable<T>> GetAllAsync(ITransaction transaction, Func<T, bool> predicate = null, CancellationToken cancellationToken = default)
        {
            // Get all from underlying repository
            var allUnderlyingEntities = await this.underlyingRepository.GetAllAsync(null, cancellationToken); // Get all, then filter in memory
            var entityDict = allUnderlyingEntities.ToDictionary(this.GetEntityKey, e => e);

            // Apply transaction operations from the current transaction
            // Considering only operations for the current transaction
            var transactionOperations = this.pendingOperations.Values
                .Where(op => op.TransactionId == transaction.TransactionId)
                .ToList();

            foreach (var operation in transactionOperations)
            {
                switch (operation.Type)
                {
                    case OperationType.Insert:
                    case OperationType.Update:
                        if (operation.NewValue != null) // Should always be non-null for Insert/Update
                        {
                            entityDict[operation.Key] = operation.NewValue;
                        }
                        break;
                    case OperationType.Delete:
                        entityDict.Remove(operation.Key);
                        break;
                    // Read operations are not directly applied here as they reflect a state already captured
                    // or are used for isolation in GetAsync.
                }
            }

            var finalEntities = entityDict.Values;
            return predicate != null ? finalEntities.Where(predicate) : finalEntities;
        }

        private void TrackOperation(string transactionId, string key, OperationType type, T originalValue, T newValue)
        {
            lock (this.operationLock)
            {
                var operation = new TransactionOperation<T>
                {
                    TransactionId = transactionId,
                    Key = key,
                    Type = type,
                    OriginalValue = originalValue,
                    NewValue = newValue,
                    Timestamp = DateTime.UtcNow
                };

                this.pendingOperations.AddOrUpdate(key, operation, (k, existing) =>
                {
                    // If this is a new transaction operation, keep the original value from the first operation
                    if (existing.TransactionId == transactionId)
                    {
                        operation.OriginalValue = existing.OriginalValue;
                    }
                    return operation;
                });
            }
        }

        private string GetEntityKey(T entity)
        {
            // Generic key extraction - looks for common key property names
            // Priority order: Id, ID, Key, key
            var keyProperties = new[] { "Id", "ID", "Key", "key" };

            foreach (var propertyName in keyProperties)
            {
                var property = typeof(T).GetProperty(propertyName);
                if (property != null && property.CanRead)
                {
                    var value = property.GetValue(entity);
                    if (value != null)
                    {
                        return value.ToString();
                    }
                }
            }

            // Fallback: use the entity's hash code as a string key
            // This ensures the method always returns a key, even for entities without conventional key properties
            this.logger.LogWarning("Entity type {EntityType} does not have a conventional key property (Id, ID, Key, key). Using hash code as key.", typeof(T).Name);
            return entity.GetHashCode().ToString();
        }

        #region ITransactionalResource Implementation

        public async Task<bool> PrepareAsync(ITransaction transaction, CancellationToken cancellationToken = default)
        {
            try
            {
                var operations = this.pendingOperations.Values
                    .Where(op => op.TransactionId == transaction.TransactionId)
                    .ToList();

                this.logger.LogDebug("Preparing {OperationCount} operations for transaction {TransactionId}",
                    operations.Count, transaction.TransactionId);

                // Validate all operations can be applied
                foreach (var operation in operations)
                {
                    if (!await this.ValidateOperationAsync(operation, cancellationToken))
                    {
                        this.logger.LogWarning("Operation validation failed for key {Key} in transaction {TransactionId}",
                            operation.Key, transaction.TransactionId);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to prepare repository for transaction {TransactionId}", transaction.TransactionId);
                return false;
            }
        }

        public async Task CommitAsync(ITransaction transaction, CancellationToken cancellationToken = default)
        {
            var operations = this.pendingOperations.Values
                .Where(op => op.TransactionId == transaction.TransactionId)
                .OrderBy(op => op.Timestamp)
                .ToList();

            this.logger.LogInformation("Committing {OperationCount} operations for transaction {TransactionId}",
                operations.Count, transaction.TransactionId);

            try
            {
                // Apply all operations to underlying repository
                foreach (var operation in operations)
                {
                    await this.ApplyOperationAsync(operation, cancellationToken);
                }

                // Clean up transaction operations
                this.CleanupTransactionOperations(transaction.TransactionId);

                this.logger.LogDebug("Successfully committed all operations for transaction {TransactionId}", transaction.TransactionId);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to commit operations for transaction {TransactionId}", transaction.TransactionId);
                throw;
            }
        }

        public async Task RollbackAsync(ITransaction transaction, CancellationToken cancellationToken = default)
        {
            var operations = this.pendingOperations.Values
                .Where(op => op.TransactionId == transaction.TransactionId)
                .ToList();

            this.logger.LogInformation("Rolling back {OperationCount} operations for transaction {TransactionId} on resource {ResourceId}",
                operations.Count, transaction.TransactionId, this.ResourceId);

            // Clean up transaction operations (no need to apply them to underlying store)
            this.CleanupTransactionOperations(transaction.TransactionId);

            this.logger.LogDebug("Successfully rolled back all operations for transaction {TransactionId} on resource {ResourceId}",
                transaction.TransactionId, this.ResourceId);
            await Task.CompletedTask;
        }

        public Task CreateSavepointAsync(ITransaction transaction, ISavepoint savepoint, CancellationToken cancellationToken = default)
        {
            var savepointKey = $"{transaction.TransactionId}_{savepoint.Name}";

            // Create a snapshot of current pending operations for this transaction
            var currentOperationsSnapshot = this.pendingOperations.Values
                .Where(op => op.TransactionId == transaction.TransactionId)
                .ToDictionary(op => op.Key, op => op.Clone()); // Clone operations to prevent modification issues

            this.savepoints.AddOrUpdate(savepointKey, currentOperationsSnapshot, (key, existing) => currentOperationsSnapshot);

            this.logger.LogDebug("Created savepoint {SavepointName} for transaction {TransactionId} on resource {ResourceId} with {OperationCount} operations snapshot",
                savepoint.Name, transaction.TransactionId, this.ResourceId, currentOperationsSnapshot.Count);

            return Task.CompletedTask;
        }

        public async Task RollbackToSavepointAsync(ITransaction transaction, ISavepoint savepoint, CancellationToken cancellationToken = default)
        {
            var savepointKey = $"{transaction.TransactionId}_{savepoint.Name}";

            if (this.savepoints.TryGetValue(savepointKey, out var savepointOperationSnapshot))
            {
                this.logger.LogInformation("Rolling back resource {ResourceId} to savepoint {SavepointName} in transaction {TransactionId}",
                    this.ResourceId, savepoint.Name, transaction.TransactionId);

                var keysForCurrentTx = this.pendingOperations
                    .Where(kvp => kvp.Value.TransactionId == transaction.TransactionId)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in keysForCurrentTx)
                {
                    this.pendingOperations.TryRemove(key, out _);
                }

                foreach (var savedOperationEntry in savepointOperationSnapshot)
                {
                    this.pendingOperations.AddOrUpdate(savedOperationEntry.Key, savedOperationEntry.Value.Clone(), (k, existing) => savedOperationEntry.Value.Clone());
                }

                // The TransactionCoordinator is responsible for identifying and instructing to discard future savepoint data.
                this.logger.LogDebug("Successfully rolled back resource {ResourceId} to savepoint {SavepointName} in transaction {TransactionId}",
                    this.ResourceId, savepoint.Name, transaction.TransactionId);
            }
            else
            {
                this.logger.LogWarning("Savepoint {SavepointName} not found for transaction {TransactionId} on resource {ResourceId}. Cannot rollback to savepoint.",
                    savepoint.Name, transaction.TransactionId, this.ResourceId);
                // Consider throwing: throw new InvalidOperationException($"Savepoint {savepoint.Name} not found for transaction {transaction.TransactionId} on resource {ResourceId}.");
            }

            await Task.CompletedTask;
        }

        public Task DiscardSavepointDataAsync(ITransaction transaction, ISavepoint savepointToDiscard, CancellationToken cancellationToken = default)
        {
            var savepointKey = $"{transaction.TransactionId}_{savepointToDiscard.Name}";
            if (this.savepoints.TryRemove(savepointKey, out _))
            {
                this.logger.LogDebug("Discarded savepoint data for {SavepointName} in transaction {TransactionId} on resource {ResourceId}",
                    savepointToDiscard.Name, transaction.TransactionId, this.ResourceId);
            }
            else
            {
                this.logger.LogDebug("No savepoint data found to discard for {SavepointName} in transaction {TransactionId} on resource {ResourceId}",
                    savepointToDiscard.Name, transaction.TransactionId, this.ResourceId);
            }
            return Task.CompletedTask;
        }

        #endregion

        private async Task<bool> ValidateOperationAsync(TransactionOperation<T> operation, CancellationToken cancellationToken)
        {
            try
            {
                // Check if the underlying data hasn't changed since we read it (optimistic concurrency)
                var currentValue = await this.underlyingRepository.GetAsync(operation.Key, cancellationToken);

                // For simplicity, we'll allow the operation if:
                // 1. It's an insert and no current value exists
                // 2. It's an update/delete and the current value matches our original value
                // In a real implementation, you might use ETags, version numbers, or timestamps

                switch (operation.Type)
                {
                    case OperationType.Insert:
                        return currentValue == null;
                    case OperationType.Update:
                    case OperationType.Delete:
                        return ReferenceEquals(currentValue, operation.OriginalValue) ||
                               (currentValue?.Equals(operation.OriginalValue) ?? operation.OriginalValue == null);
                    case OperationType.Read:
                        return true; // Reads don't need validation
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to validate operation for key {Key}", operation.Key);
                return false;
            }
        }

        private async Task ApplyOperationAsync(TransactionOperation<T> operation, CancellationToken cancellationToken)
        {
            switch (operation.Type)
            {
                case OperationType.Insert:
                case OperationType.Update:
                    await this.underlyingRepository.SaveAsync(operation.Key, operation.NewValue, cancellationToken);
                    break;
                case OperationType.Delete:
                    await this.underlyingRepository.DeleteAsync(operation.Key, cancellationToken);
                    break;
                case OperationType.Read:
                    // No action needed for reads
                    break;
            }
        }

        private void CleanupTransactionOperations(string transactionId)
        {
            var keysToRemove = this.pendingOperations.Values
                .Where(op => op.TransactionId == transactionId)
                .Select(op => op.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                this.pendingOperations.TryRemove(key, out _);
            }

            // Clean up savepoints for this transaction
            var savepointKeysToRemove = this.savepoints.Keys
                .Where(key => key.StartsWith($"{transactionId}_"))
                .ToList();

            foreach (var key in savepointKeysToRemove)
            {
                this.savepoints.TryRemove(key, out _);
            }
        }
    }


}