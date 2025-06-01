//-------------------------------------------------------------------------------
// <copyright file="TransactionCoordinator.cs" company="Microsoft Corp.">
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
    /// Core transaction implementation with 2-phase commit and rollback enforcement
    /// </summary>
    public class TransactionCoordinator : ITransaction
    {
        private readonly ILogger<TransactionCoordinator> _logger;
        private readonly ConcurrentDictionary<string, ITransactionalResource> _enlistedResources;
        private readonly ConcurrentDictionary<string, ISavepoint> _savepoints;
        private readonly List<Func<TransactionState, Task>> _completionCallbacks;
        private readonly Timer _timeoutTimer;
        private readonly object _stateLock = new object();
        private readonly TransactionOptions _options;

        private TransactionState _state;
        private bool _disposed;
        private readonly CancellationTokenSource _cancellationTokenSource;

        public string TransactionId { get; }
        public TransactionState State
        {
            get
            {
                lock (_stateLock)
                {
                    return _state;
                }
            }
            private set
            {
                lock (_stateLock)
                {
                    _state = value;
                }
            }
        }
        public IsolationLevel IsolationLevel { get; }
        public TimeSpan Timeout { get; }
        public DateTime CreatedAt { get; }

        public TransactionCoordinator(ILogger<TransactionCoordinator> logger, TransactionOptions options = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? new TransactionOptions();

            TransactionId = Guid.NewGuid().ToString("N");
            IsolationLevel = _options.IsolationLevel;
            Timeout = _options.Timeout;
            CreatedAt = DateTime.UtcNow;
            State = TransactionState.Active;

            _enlistedResources = new ConcurrentDictionary<string, ITransactionalResource>();
            _savepoints = new ConcurrentDictionary<string, ISavepoint>();
            _completionCallbacks = new List<Func<TransactionState, Task>>();
            _cancellationTokenSource = new CancellationTokenSource();

            // Setup timeout timer
            _timeoutTimer = new Timer(OnTimeout, null, Timeout, TimeSpan.FromMilliseconds(-1));

            _logger.LogDebug("Created transaction {TransactionId} with timeout {Timeout}", TransactionId, Timeout);
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token);

            lock (_stateLock)
            {
                if (State != TransactionState.Active)
                {
                    throw new InvalidOperationException($"Cannot commit transaction in state {State}");
                }
                State = TransactionState.Preparing;
            }

            _logger.LogInformation("Committing transaction {TransactionId} with {ResourceCount} resources",
                TransactionId, _enlistedResources.Count);

            try
            {
                // Phase 1: Prepare all resources (2-phase commit)
                await PreparePhaseAsync(combinedCts.Token);

                lock (_stateLock)
                {
                    if (State == TransactionState.Preparing)
                    {
                        State = TransactionState.Prepared;
                    }
                }

                // Phase 2: Commit all resources
                await CommitPhaseAsync(combinedCts.Token);

                lock (_stateLock)
                {
                    State = TransactionState.Committed;
                }

                _logger.LogInformation("Successfully committed transaction {TransactionId}", TransactionId);
                await ExecuteCompletionCallbacksAsync(TransactionState.Committed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to commit transaction {TransactionId}", TransactionId);

                // Rollback on commit failure
                await RollbackInternalAsync(combinedCts.Token, TransactionState.Failed);
                throw;
            }
            finally
            {
                _timeoutTimer?.Change(System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan); // Disable timer
            }
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token);
            await RollbackInternalAsync(combinedCts.Token, TransactionState.RolledBack);
        }

        private async Task RollbackInternalAsync(CancellationToken cancellationToken, TransactionState finalState)
        {
            lock (_stateLock)
            {
                if (State == TransactionState.Committed || State == TransactionState.RolledBack)
                {
                    return; // Already completed
                }
                State = TransactionState.RollingBack;
            }

            _logger.LogInformation("Rolling back transaction {TransactionId} with {ResourceCount} resources",
                TransactionId, _enlistedResources.Count);

            var rollbackTasks = new List<Task>();
            var exceptions = new List<Exception>();

            // Rollback all enlisted resources in parallel
            foreach (var resource in _enlistedResources.Values)
            {
                rollbackTasks.Add(RollbackResourceSafely(resource, cancellationToken, exceptions));
            }

            // Wait for all rollbacks to complete
            await Task.WhenAll(rollbackTasks);

            lock (_stateLock)
            {
                State = finalState;
            }

            // Log any rollback failures but don't throw - rollback must succeed
            if (exceptions.Any())
            {
                var aggregateException = new AggregateException(exceptions);
                _logger.LogError(aggregateException, "Some resources failed to rollback in transaction {TransactionId}", TransactionId);
            }

            _logger.LogInformation("Completed rollback for transaction {TransactionId}", TransactionId);
            await ExecuteCompletionCallbacksAsync(finalState);
        }

        private async Task RollbackResourceSafely(ITransactionalResource resource, CancellationToken cancellationToken, List<Exception> exceptions)
        {
            try
            {
                await resource.RollbackAsync(this, cancellationToken);
                _logger.LogDebug("Successfully rolled back resource {ResourceId} in transaction {TransactionId}",
                    resource.ResourceId, TransactionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rollback resource {ResourceId} in transaction {TransactionId}",
                    resource.ResourceId, TransactionId);

                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            }
        }

        public async Task<ISavepoint> CreateSavepointAsync(string name, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (State != TransactionState.Active)
            {
                throw new InvalidOperationException($"Cannot create savepoint in transaction state {State}");
            }

            // Ensure savepoint name is unique for this transaction
            // (The TransactionalRepository uses a combined key, but coordinator should also ensure uniqueness for its own tracking)
            if (_savepoints.ContainsKey(name)) // Assuming _savepoints keys by name for the current transaction
            {
                 throw new InvalidOperationException($"Savepoint '{name}' already exists in transaction {TransactionId}");
            }

            var savepoint = new Savepoint(name, TransactionId, DateTime.UtcNow);

            // Notify all enlisted resources to create their own savepoint marker
            var resourceSavepointTasks = new List<Task>();
            foreach (var resource in _enlistedResources.Values)
            {
                resourceSavepointTasks.Add(resource.CreateSavepointAsync(this, savepoint, cancellationToken));
            }
            await Task.WhenAll(resourceSavepointTasks); // Wait for all resources to acknowledge

            if (!_savepoints.TryAdd(name, savepoint)) // Store savepoint in coordinator AFTER resources confirmed
            {
                // This should ideally not happen if the initial check passed and operations are atomic enough,
                // but as a safeguard:
                _logger.LogWarning("Failed to add savepoint {SavepointName} to coordinator tracking for transaction {TransactionId} after resources created it. This might indicate a concurrency issue.", name, TransactionId);
                // Potentially attempt to tell resources to discard this savepoint if critical
                throw new InvalidOperationException($"Savepoint '{name}' could not be added to coordinator, though resources may have created it.");
            }

            _logger.LogDebug("Created savepoint {SavepointName} in transaction {TransactionId} and notified {ResourceCount} resources",
                name, TransactionId, _enlistedResources.Count);
            return savepoint;
        }

        public async Task RollbackToSavepointAsync(ISavepoint savepoint, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (State != TransactionState.Active)
            {
                throw new InvalidOperationException($"Cannot rollback to savepoint in transaction state {State}");
            }

            if (savepoint?.TransactionId != TransactionId)
            {
                throw new InvalidOperationException("Savepoint belongs to a different transaction or is null.");
            }

            if (!_savepoints.TryGetValue(savepoint.Name, out var targetSavepointInfo) || targetSavepointInfo.TransactionId != TransactionId)
            {
                 throw new InvalidOperationException($"Savepoint '{savepoint.Name}' not found or invalid for this transaction.");
            }

            _logger.LogInformation("Rolling back to savepoint {SavepointName} in transaction {TransactionId}",
                savepoint.Name, TransactionId);

            // Tell all resources to rollback to this savepoint state
            var resourceRollbackTasks = new List<Task>();
            var exceptions = new List<Exception>(); // To collect exceptions from resource rollbacks

            foreach (var resource in _enlistedResources.Values)
            {
                resourceRollbackTasks.Add(RollbackResourceToSavepointSafely(resource, savepoint, cancellationToken, exceptions));
            }
            await Task.WhenAll(resourceRollbackTasks);

            if (exceptions.Any())
            {
                // If any resource fails to rollback to savepoint, the transaction state is uncertain.
                // This is a critical failure. The transaction should probably move to a Failed state.
                _logger.LogError(new AggregateException(exceptions), "One or more resources failed to rollback to savepoint {SavepointName} in transaction {TransactionId}. Transaction integrity compromised.", savepoint.Name, TransactionId);
                State = TransactionState.Failed; // Mark transaction as failed
                // Do not attempt to commit or further rollback this transaction automatically. Manual intervention might be needed.
                throw new TransactionException($"Failed to rollback to savepoint {savepoint.Name} due to resource errors. Transaction is now in a Failed state.", new AggregateException(exceptions));
            }

            // Identify savepoints created after the target savepoint for this transaction
            var savepointsToDiscard = _savepoints
                .Where(kvp => kvp.Value.TransactionId == TransactionId && kvp.Value.CreatedAt > savepoint.CreatedAt)
                .Select(kvp => kvp.Value) // Select the ISavepoint object
                .ToList();

            // Tell resources to discard data for these subsequent savepoints
            var discardTasks = new List<Task>();
            foreach (var spToDiscard in savepointsToDiscard)
            {
                foreach (var resource in _enlistedResources.Values)
                {
                    // It's possible a resource doesn't have data for a savepoint if it was enlisted after the savepoint was created.
                    // DiscardSavepointDataAsync should handle this gracefully (e.g., log and continue).
                    discardTasks.Add(resource.DiscardSavepointDataAsync(this, spToDiscard, cancellationToken));
                }
                _savepoints.TryRemove(spToDiscard.Name, out _); // Remove from coordinator's list
                _logger.LogDebug("Discarded subsequent savepoint {SavepointName} after rolling back to {TargetSavepointName} in transaction {TransactionId}",
                    spToDiscard.Name, savepoint.Name, TransactionId);
            }
            await Task.WhenAll(discardTasks); // Wait for discard operations to complete

            _logger.LogInformation("Successfully rolled back transaction {TransactionId} to savepoint {SavepointName}",
                TransactionId, savepoint.Name);

            // Note: Transaction remains Active. It can proceed and be committed or rolled back entirely later.
        }

        private async Task RollbackResourceToSavepointSafely(ITransactionalResource resource, ISavepoint savepoint,
            CancellationToken cancellationToken, List<Exception> exceptions)
        {
            try
            {
                await resource.RollbackToSavepointAsync(this, savepoint, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rollback resource {ResourceId} to savepoint {SavepointName} in transaction {TransactionId}",
                    resource.ResourceId, savepoint.Name, TransactionId);

                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            }
        }

        public void EnlistResource(ITransactionalResource resource)
        {
            ThrowIfDisposed();

            if (State != TransactionState.Active)
            {
                throw new InvalidOperationException($"Cannot enlist resource in transaction state {State}");
            }

            if (!_enlistedResources.TryAdd(resource.ResourceId, resource))
            {
                throw new InvalidOperationException($"Resource {resource.ResourceId} is already enlisted");
            }

            _logger.LogDebug("Enlisted resource {ResourceId} in transaction {TransactionId}", resource.ResourceId, TransactionId);
        }

        public IEnumerable<T> GetEnlistedResources<T>() where T : ITransactionalResource
        {
            return _enlistedResources.Values.OfType<T>();
        }

        public void AddCompletionCallback(Func<TransactionState, Task> callback)
        {
            ThrowIfDisposed();

            lock (_completionCallbacks)
            {
                _completionCallbacks.Add(callback);
            }
        }

        private async Task PreparePhaseAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Starting prepare phase for transaction {TransactionId}", TransactionId);
            // State = TransactionState.Preparing; // State is already set in CommitAsync

            var prepareTasks = _enlistedResources.Values.Select(resource =>
                PrepareResourceSafely(resource, cancellationToken /* Pass token */)
            );

            try
            {
                var results = await Task.WhenAll(prepareTasks);
                if (results.Any(r => !r))
                {
                    // If any resource failed to prepare, an exception would have been thrown by PrepareResourceSafely
                    // This path indicates a resource returned false without an exception, which is also a prepare failure.
                    throw new TransactionException($"One or more resources failed to prepare for transaction {TransactionId}.");
                }
                _logger.LogDebug("All resources prepared successfully for transaction {TransactionId}", TransactionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during prepare phase for transaction {TransactionId}", TransactionId);
                // Do not change state here, CommitAsync will handle rollback.
                throw; // Re-throw to be caught by CommitAsync
            }
        }

        private async Task<bool> PrepareResourceSafely(ITransactionalResource resource, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("Preparing resource {ResourceId} in transaction {TransactionId}", resource.ResourceId, TransactionId);
                var prepared = await resource.PrepareAsync(this, cancellationToken);
                if (!prepared)
                {
                    _logger.LogWarning("Resource {ResourceId} reported unsuccessful prepare in transaction {TransactionId}", resource.ResourceId, TransactionId);
                    // Throw an exception if prepare returns false, to ensure it's treated as a failure.
                    throw new TransactionException($"Resource {resource.ResourceId} failed to prepare.");
                }
                _logger.LogDebug("Successfully prepared resource {ResourceId} in transaction {TransactionId}", resource.ResourceId, TransactionId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to prepare resource {ResourceId} in transaction {TransactionId}", resource.ResourceId, TransactionId);
                // Re-throw the exception to be caught by PreparePhaseAsync and then CommitAsync
                throw;
            }
        }

        private async Task CommitPhaseAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Starting commit phase for transaction {TransactionId}", TransactionId);
            // State = TransactionState.Committing; // State is already set in CommitAsync

            var commitTasks = _enlistedResources.Values.Select(resource =>
                CommitResourceSafely(resource, cancellationToken /* Pass token */)
            );

            try
            {
                await Task.WhenAll(commitTasks);
                _logger.LogDebug("All resources committed successfully for transaction {TransactionId}", TransactionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during commit phase for transaction {TransactionId}", TransactionId);
                // Do not change state here, CommitAsync will handle rollback.
                throw; // Re-throw to be caught by CommitAsync
            }
        }

        private async Task CommitResourceSafely(ITransactionalResource resource, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("Committing resource {ResourceId} in transaction {TransactionId}", resource.ResourceId, TransactionId);
                await resource.CommitAsync(this, cancellationToken);
                _logger.LogDebug("Successfully committed resource {ResourceId} in transaction {TransactionId}", resource.ResourceId, TransactionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to commit resource {ResourceId} in transaction {TransactionId}", resource.ResourceId, TransactionId);
                // Re-throw the exception to be caught by CommitPhaseAsync and then CommitAsync
                throw;
            }
        }

        private async Task ExecuteCompletionCallbacksAsync(TransactionState finalState)
        {
            List<Func<TransactionState, Task>> callbacks;
            lock (_completionCallbacks)
            {
                callbacks = new List<Func<TransactionState, Task>>(_completionCallbacks);
            }

            foreach (var callback in callbacks)
            {
                try
                {
                    await callback(finalState);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Completion callback failed for transaction {TransactionId}", TransactionId);
                }
            }
        }

        private void OnTimeout(object state)
        {
            _logger.LogWarning("Transaction {TransactionId} timed out after {Timeout}", TransactionId, Timeout);

            _cancellationTokenSource.Cancel();

            lock (_stateLock)
            {
                if (State == TransactionState.Active || State == TransactionState.Preparing)
                {
                    State = TransactionState.Timeout;
                }
            }

            // Trigger async rollback due to timeout
            Task.Run(async () =>
            {
                try
                {
                    await RollbackInternalAsync(CancellationToken.None, TransactionState.Timeout);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to rollback timed out transaction {TransactionId}", TransactionId);
                }
            });
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TransactionCoordinator));
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _logger.LogDebug("Disposing transaction {TransactionId}", TransactionId);

                // Stop the timeout timer
                _timeoutTimer?.Change(System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan);
                _timeoutTimer?.Dispose();

                // Cancel any ongoing operations
                if (!_cancellationTokenSource.IsCancellationRequested)
                {
                    _cancellationTokenSource.Cancel();
                }
                _cancellationTokenSource.Dispose();

                // Handle auto-rollback if configured and transaction is still active
                if (_options.AutoRollbackOnDispose && State == TransactionState.Active)
                {
                    _logger.LogInformation("Auto-rolling back transaction {TransactionId} on dispose", TransactionId);
                    try
                    {
                        // Calling async method synchronously in Dispose is generally discouraged.
                        // This can lead to deadlocks if the async method awaits on something that requires the current thread.
                        // A better pattern might be to ensure CommitAsync or RollbackAsync is always called,
                        // or to make Dispose async (which changes the IDisposable contract and is not standard).
                        // For this specific case, if RollbackInternalAsync is truly safe to run this way, it might be acceptable.
                        // However, one should be very cautious.
                        RollbackInternalAsync(_cancellationTokenSource.Token, TransactionState.RolledBack).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during auto-rollback on dispose for transaction {TransactionId}", TransactionId);
                        State = TransactionState.Failed;
                    }
                }
            }

            _disposed = true;
        }

        // Destructor (finalizer)
        ~TransactionCoordinator()
        {
            Dispose(false);
        }
    }
}