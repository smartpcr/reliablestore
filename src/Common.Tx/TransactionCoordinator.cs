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
        private readonly ILogger<TransactionCoordinator> logger;
        private readonly ConcurrentDictionary<string, ITransactionalResource> enlistedResources;
        private readonly ConcurrentDictionary<string, ISavepoint> savepoints;
        private readonly List<Func<TransactionState, Task>> completionCallbacks;
        private readonly Timer timeoutTimer;
        private readonly object stateLock = new object();
        private volatile bool isDisposing = false;
        private readonly TransactionOptions options;

        private TransactionState state;
        private bool disposed;
        private readonly CancellationTokenSource cancellationTokenSource;

        public string TransactionId { get; }
        public TransactionState State
        {
            get
            {
                lock (this.stateLock)
                {
                    return this.state;
                }
            }
            private set
            {
                lock (this.stateLock)
                {
                    this.state = value;
                }
            }
        }
        public IsolationLevel IsolationLevel { get; }
        public TimeSpan Timeout { get; }
        public DateTime CreatedAt { get; }

        public TransactionCoordinator(ILogger<TransactionCoordinator> logger, TransactionOptions options = null)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.options = options ?? new TransactionOptions();

            this.TransactionId = Guid.NewGuid().ToString("N");
            this.IsolationLevel = this.options.IsolationLevel;
            this.Timeout = this.options.Timeout;
            this.CreatedAt = DateTime.UtcNow;
            this.State = TransactionState.Active;

            this.enlistedResources = new ConcurrentDictionary<string, ITransactionalResource>();
            this.savepoints = new ConcurrentDictionary<string, ISavepoint>();
            this.completionCallbacks = new List<Func<TransactionState, Task>>();
            this.cancellationTokenSource = new CancellationTokenSource();

            // Setup timeout timer
            this.timeoutTimer = new Timer(this.OnTimeout, null, this.Timeout, System.Threading.Timeout.InfiniteTimeSpan);

            this.logger.LogDebug("Created transaction {TransactionId} with timeout {Timeout}", this.TransactionId, this.Timeout);
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();

            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.cancellationTokenSource.Token);

            lock (this.stateLock)
            {
                this.ThrowIfDisposed();
                if (this.state != TransactionState.Active)
                {
                    throw new InvalidOperationException($"Cannot commit transaction in state {this.state}");
                }
                this.state = TransactionState.Preparing;
            }

            this.logger.LogInformation("Committing transaction {TransactionId} with {ResourceCount} resources",
                this.TransactionId, this.enlistedResources.Count);

            try
            {
                // Phase 1: Prepare all resources (2-phase commit)
                await this.PreparePhaseAsync(combinedCts.Token);

                lock (this.stateLock)
                {
                    if (this.state == TransactionState.Preparing)
                    {
                        this.state = TransactionState.Prepared;
                    }
                }

                // Phase 2: Commit all resources
                lock (this.stateLock)
                {
                    this.state = TransactionState.Committing;
                }
                
                await this.CommitPhaseAsync(combinedCts.Token);

                lock (this.stateLock)
                {
                    this.state = TransactionState.Committed;
                }

                this.logger.LogInformation("Successfully committed transaction {TransactionId}", this.TransactionId);
                await this.ExecuteCompletionCallbacksAsync(TransactionState.Committed);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to commit transaction {TransactionId}", this.TransactionId);

                // Rollback on commit failure
                await this.RollbackInternalAsync(combinedCts.Token, TransactionState.Failed);
                throw;
            }
            finally
            {
                this.timeoutTimer?.Change(System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan); // Disable timer
            }
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();

            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.cancellationTokenSource.Token);
            await this.RollbackInternalAsync(combinedCts.Token, TransactionState.RolledBack);
        }

        private async Task RollbackInternalAsync(CancellationToken cancellationToken, TransactionState finalState)
        {
            lock (this.stateLock)
            {
                if (this.state == TransactionState.Committed || this.state == TransactionState.RolledBack)
                {
                    return; // Already completed
                }
                this.state = TransactionState.RollingBack;
            }

            this.logger.LogInformation("Rolling back transaction {TransactionId} with {ResourceCount} resources",
                this.TransactionId, this.enlistedResources.Count);

            var rollbackTasks = new List<Task>();
            var exceptions = new List<Exception>();

            // Rollback all enlisted resources in parallel
            var resources = this.enlistedResources.Values.ToList(); // Snapshot to avoid enumeration issues
            foreach (var resource in resources)
            {
                rollbackTasks.Add(this.RollbackResourceSafely(resource, cancellationToken, exceptions));
            }

            // Wait for all rollbacks to complete
            await Task.WhenAll(rollbackTasks);

            lock (this.stateLock)
            {
                this.state = finalState;
            }

            // Log any rollback failures but don't throw - rollback must succeed
            if (exceptions.Any())
            {
                var aggregateException = new AggregateException(exceptions);
                this.logger.LogError(aggregateException, "Some resources failed to rollback in transaction {TransactionId}", this.TransactionId);
            }

            await this.ExecuteCompletionCallbacksAsync(finalState);
            this.logger.LogInformation("Completed rollback for transaction {TransactionId}", this.TransactionId);
        }

        private async Task RollbackResourceSafely(ITransactionalResource resource, CancellationToken cancellationToken, List<Exception> exceptions)
        {
            try
            {
                await resource.RollbackAsync(this, cancellationToken);
                this.logger.LogDebug("Successfully rolled back resource {ResourceId} in transaction {TransactionId}",
                    resource.ResourceId, this.TransactionId);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to rollback resource {ResourceId} in transaction {TransactionId}",
                    resource.ResourceId, this.TransactionId);

                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            }
        }

        public async Task<ISavepoint> CreateSavepointAsync(string name, CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();

            if (this.State != TransactionState.Active)
            {
                throw new InvalidOperationException($"Cannot create savepoint in transaction state {this.State}");
            }

            // Ensure savepoint name is unique for this transaction
            // (The TransactionalRepository uses a combined key, but coordinator should also ensure uniqueness for its own tracking)
            if (this.savepoints.ContainsKey(name)) // Assuming savepoints keys by name for the current transaction
            {
                 throw new InvalidOperationException($"Savepoint '{name}' already exists in transaction {this.TransactionId}");
            }

            var savepoint = new Savepoint(name, this.TransactionId, DateTime.UtcNow);

            // Notify all enlisted resources to create their own savepoint marker
            var resourceSavepointTasks = new List<Task>();
            var resources = this.enlistedResources.Values.ToList(); // Snapshot to avoid enumeration issues
            foreach (var resource in resources)
            {
                resourceSavepointTasks.Add(resource.CreateSavepointAsync(this, savepoint, cancellationToken));
            }
            await Task.WhenAll(resourceSavepointTasks); // Wait for all resources to acknowledge

            if (!this.savepoints.TryAdd(name, savepoint)) // Store savepoint in coordinator AFTER resources confirmed
            {
                // This should ideally not happen if the initial check passed and operations are atomic enough,
                // but as a safeguard:
                this.logger.LogWarning("Failed to add savepoint {SavepointName} to coordinator tracking for transaction {TransactionId} after resources created it. This might indicate a concurrency issue.", name, this.TransactionId);
                // Potentially attempt to tell resources to discard this savepoint if critical
                throw new InvalidOperationException($"Savepoint '{name}' could not be added to coordinator, though resources may have created it.");
            }

            this.logger.LogDebug("Created savepoint {SavepointName} in transaction {TransactionId} and notified {ResourceCount} resources",
                name, this.TransactionId, this.enlistedResources.Count);
            return savepoint;
        }

        public async Task RollbackToSavepointAsync(ISavepoint savepoint, CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();

            if (this.State != TransactionState.Active)
            {
                throw new InvalidOperationException($"Cannot rollback to savepoint in transaction state {this.State}");
            }

            if (savepoint?.TransactionId != this.TransactionId)
            {
                throw new InvalidOperationException("Savepoint belongs to a different transaction or is null.");
            }

            if (!this.savepoints.TryGetValue(savepoint.Name, out var targetSavepointInfo) || targetSavepointInfo.TransactionId != this.TransactionId)
            {
                 throw new InvalidOperationException($"Savepoint '{savepoint.Name}' not found or invalid for this transaction.");
            }

            this.logger.LogInformation("Rolling back to savepoint {SavepointName} in transaction {TransactionId}",
                savepoint.Name, this.TransactionId);

            // Tell all resources to rollback to this savepoint state
            var resourceRollbackTasks = new List<Task>();
            var exceptions = new List<Exception>(); // To collect exceptions from resource rollbacks
            var resources = this.enlistedResources.Values.ToList(); // Snapshot to avoid enumeration issues

            foreach (var resource in resources)
            {
                resourceRollbackTasks.Add(this.RollbackResourceToSavepointSafely(resource, savepoint, cancellationToken, exceptions));
            }
            await Task.WhenAll(resourceRollbackTasks);

            if (exceptions.Any())
            {
                // If any resource fails to rollback to savepoint, the transaction state is uncertain.
                // This is a critical failure. The transaction should probably move to a Failed state.
                this.logger.LogError(new AggregateException(exceptions), "One or more resources failed to rollback to savepoint {SavepointName} in transaction {TransactionId}. Transaction integrity compromised.", savepoint.Name, this.TransactionId);
                lock (this.stateLock)
                {
                    this.state = TransactionState.Failed; // Mark transaction as failed
                }
                // Do not attempt to commit or further rollback this transaction automatically. Manual intervention might be needed.
                throw new TransactionException($"Failed to rollback to savepoint {savepoint.Name} due to resource errors. Transaction is now in a Failed state.", new AggregateException(exceptions));
            }

            // Identify savepoints created after the target savepoint for this transaction
            var savepointsToDiscard = this.savepoints
                .Where(kvp => kvp.Value.TransactionId == this.TransactionId && kvp.Value.CreatedAt > savepoint.CreatedAt)
                .Select(kvp => kvp.Value) // Select the ISavepoint object
                .ToList();

            // Tell resources to discard data for these subsequent savepoints
            var discardTasks = new List<Task>();
            var currentResources = this.enlistedResources.Values.ToList(); // Snapshot
            foreach (var spToDiscard in savepointsToDiscard)
            {
                foreach (var resource in currentResources)
                {
                    // It's possible a resource doesn't have data for a savepoint if it was enlisted after the savepoint was created.
                    // DiscardSavepointDataAsync should handle this gracefully (e.g., log and continue).
                    discardTasks.Add(resource.DiscardSavepointDataAsync(this, spToDiscard, cancellationToken));
                }
                this.savepoints.TryRemove(spToDiscard.Name, out _); // Remove from coordinator's list
                this.logger.LogDebug("Discarded subsequent savepoint {SavepointName} after rolling back to {TargetSavepointName} in transaction {TransactionId}",
                    spToDiscard.Name, savepoint.Name, this.TransactionId);
            }
            await Task.WhenAll(discardTasks); // Wait for discard operations to complete

            this.logger.LogInformation("Successfully rolled back transaction {TransactionId} to savepoint {SavepointName}",
                this.TransactionId, savepoint.Name);

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
                this.logger.LogError(ex, "Failed to rollback resource {ResourceId} to savepoint {SavepointName} in transaction {TransactionId}",
                    resource.ResourceId, savepoint.Name, this.TransactionId);

                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            }
        }

        public void EnlistResource(ITransactionalResource resource)
        {
            this.ThrowIfDisposed();

            if (resource == null)
            {
                throw new ArgumentNullException(nameof(resource));
            }

            if (this.State != TransactionState.Active)
            {
                throw new InvalidOperationException($"Cannot enlist resource in transaction state {this.State}");
            }

            if (!this.enlistedResources.TryAdd(resource.ResourceId, resource))
            {
                throw new InvalidOperationException($"Resource {resource.ResourceId} is already enlisted");
            }

            this.logger.LogDebug("Enlisted resource {ResourceId} in transaction {TransactionId}", resource.ResourceId, this.TransactionId);
        }

        public IEnumerable<T> GetEnlistedResources<T>() where T : ITransactionalResource
        {
            return this.enlistedResources.Values.OfType<T>();
        }

        public void AddCompletionCallback(Func<TransactionState, Task> callback)
        {
            this.ThrowIfDisposed();

            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            lock (this.completionCallbacks)
            {
                this.completionCallbacks.Add(callback);
            }
        }

        private async Task PreparePhaseAsync(CancellationToken cancellationToken)
        {
            this.logger.LogDebug("Starting prepare phase for transaction {TransactionId}", this.TransactionId);
            // State = TransactionState.Preparing; // State is already set in CommitAsync

            var resources = this.enlistedResources.Values.ToList(); // Snapshot to avoid enumeration issues
            var prepareTasks = resources.Select(resource =>
                this.PrepareResourceSafely(resource, cancellationToken /* Pass token */)
            );

            try
            {
                var results = await Task.WhenAll(prepareTasks);
                if (results.Any(r => !r))
                {
                    // If any resource failed to prepare, an exception would have been thrown by PrepareResourceSafely
                    // This path indicates a resource returned false without an exception, which is also a prepare failure.
                    throw new TransactionException($"One or more resources failed to prepare for transaction {this.TransactionId}.");
                }
                this.logger.LogDebug("All resources prepared successfully for transaction {TransactionId}", this.TransactionId);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Error during prepare phase for transaction {TransactionId}", this.TransactionId);
                // Do not change state here, CommitAsync will handle rollback.
                throw; // Re-throw to be caught by CommitAsync
            }
        }

        private async Task<bool> PrepareResourceSafely(ITransactionalResource resource, CancellationToken cancellationToken)
        {
            var resourceTimeout = this.options.ResourceOperationTimeout ?? TimeSpan.FromMinutes(2);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(resourceTimeout);

            try
            {
                this.logger.LogDebug("Preparing resource {ResourceId} in transaction {TransactionId}", resource.ResourceId, this.TransactionId);
                
                var prepared = await resource.PrepareAsync(this, timeoutCts.Token);
                if (!prepared)
                {
                    this.logger.LogWarning("Resource {ResourceId} reported unsuccessful prepare in transaction {TransactionId}", resource.ResourceId, this.TransactionId);
                    throw new TransactionException($"Resource {resource.ResourceId} failed to prepare.");
                }
                this.logger.LogDebug("Successfully prepared resource {ResourceId} in transaction {TransactionId}", resource.ResourceId, this.TransactionId);
                return true;
            }
            catch (OperationCanceledException ex) when (timeoutCts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                this.logger.LogWarning("Prepare operation timed out for resource {ResourceId} in transaction {TransactionId} after {Timeout}", resource.ResourceId, this.TransactionId, resourceTimeout);
                throw new TransactionTimeoutException($"Resource {resource.ResourceId} prepare operation timed out after {resourceTimeout}", ex);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                this.logger.LogWarning("Prepare operation was cancelled for resource {ResourceId} in transaction {TransactionId}", resource.ResourceId, this.TransactionId);
                throw;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to prepare resource {ResourceId} in transaction {TransactionId}", resource.ResourceId, this.TransactionId);
                throw;
            }
        }

        private async Task CommitPhaseAsync(CancellationToken cancellationToken)
        {
            this.logger.LogDebug("Starting commit phase for transaction {TransactionId}", this.TransactionId);
            // State = TransactionState.Committing; // State is already set in CommitAsync

            var resources = this.enlistedResources.Values.ToList(); // Snapshot to avoid enumeration issues
            var commitTasks = resources.Select(resource =>
                this.CommitResourceSafely(resource, cancellationToken /* Pass token */)
            );

            try
            {
                await Task.WhenAll(commitTasks);
                this.logger.LogDebug("All resources committed successfully for transaction {TransactionId}", this.TransactionId);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Error during commit phase for transaction {TransactionId}", this.TransactionId);
                // Do not change state here, CommitAsync will handle rollback.
                throw; // Re-throw to be caught by CommitAsync
            }
        }

        private async Task CommitResourceSafely(ITransactionalResource resource, CancellationToken cancellationToken)
        {
            var resourceTimeout = this.options.ResourceOperationTimeout ?? TimeSpan.FromMinutes(2);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(resourceTimeout);

            try
            {
                this.logger.LogDebug("Committing resource {ResourceId} in transaction {TransactionId}", resource.ResourceId, this.TransactionId);
                
                await resource.CommitAsync(this, timeoutCts.Token);
                this.logger.LogDebug("Successfully committed resource {ResourceId} in transaction {TransactionId}", resource.ResourceId, this.TransactionId);
            }
            catch (OperationCanceledException ex) when (timeoutCts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                this.logger.LogWarning("Commit operation timed out for resource {ResourceId} in transaction {TransactionId} after {Timeout}", resource.ResourceId, this.TransactionId, resourceTimeout);
                throw new TransactionTimeoutException($"Resource {resource.ResourceId} commit operation timed out after {resourceTimeout}", ex);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                this.logger.LogWarning("Commit operation was cancelled for resource {ResourceId} in transaction {TransactionId}", resource.ResourceId, this.TransactionId);
                throw;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to commit resource {ResourceId} in transaction {TransactionId}", resource.ResourceId, this.TransactionId);
                throw;
            }
        }

        private async Task ExecuteCompletionCallbacksAsync(TransactionState finalState)
        {
            List<Func<TransactionState, Task>> callbacks;
            lock (this.completionCallbacks)
            {
                callbacks = new List<Func<TransactionState, Task>>(this.completionCallbacks);
            }

            foreach (var callback in callbacks)
            {
                try
                {
                    await callback(finalState);
                }
                catch (Exception ex)
                {
                    this.logger.LogWarning(ex, "Completion callback failed for transaction {TransactionId}", this.TransactionId);
                }
            }
        }

        private void OnTimeout(object state)
        {
            this.logger.LogWarning("Transaction {TransactionId} timed out after {Timeout}", this.TransactionId, this.Timeout);

            this.cancellationTokenSource.Cancel();

            lock (this.stateLock)
            {
                if (this.state == TransactionState.Active || this.state == TransactionState.Preparing)
                {
                    this.state = TransactionState.Timeout;
                }
            }

            // Trigger async rollback due to timeout
            Task.Run(async () =>
            {
                try
                {
                    await this.RollbackInternalAsync(CancellationToken.None, TransactionState.Timeout);
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, "Failed to rollback timed out transaction {TransactionId}", this.TransactionId);
                }
            });
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed || this.isDisposing)
            {
                throw new ObjectDisposedException(nameof(TransactionCoordinator));
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore();
            Dispose(false); // Clean up unmanaged resources only
            GC.SuppressFinalize(this);
        }

        protected virtual async ValueTask DisposeAsyncCore()
        {
            if (this.disposed || this.isDisposing)
            {
                return;
            }

            this.isDisposing = true;
            this.logger.LogDebug("Disposing transaction {TransactionId} asynchronously", this.TransactionId);

            // Stop the timeout timer
            this.timeoutTimer?.Change(System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan);
            this.timeoutTimer?.Dispose();

            // Cancel any ongoing operations
            if (!this.cancellationTokenSource.IsCancellationRequested)
            {
                this.cancellationTokenSource.Cancel();
            }

            // Handle auto-rollback if configured and transaction is still active
            if (this.options.AutoRollbackOnDispose && this.State == TransactionState.Active)
            {
                this.logger.LogInformation("Auto-rolling back transaction {TransactionId} on async dispose", this.TransactionId);
                try
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await this.RollbackInternalAsync(timeoutCts.Token, TransactionState.RolledBack);
                }
                catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
                {
                    this.logger.LogWarning("Auto-rollback timed out during async dispose for transaction {TransactionId}", this.TransactionId);
                    lock (this.stateLock)
                    {
                        this.state = TransactionState.Failed;
                    }
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, "Error during auto-rollback on async dispose for transaction {TransactionId}", this.TransactionId);
                    lock (this.stateLock)
                    {
                        this.state = TransactionState.Failed;
                    }
                }
            }

            // Clear collections to prevent memory leaks
            this.savepoints.Clear();
            lock (this.completionCallbacks)
            {
                this.completionCallbacks.Clear();
            }

            this.cancellationTokenSource.Dispose();
            this.disposed = true;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (this.disposed)
            {
                return;
            }

            if (disposing)
            {
                this.isDisposing = true;
                this.logger.LogDebug("Disposing transaction {TransactionId}", this.TransactionId);

                // Stop the timeout timer
                this.timeoutTimer?.Change(System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan);
                this.timeoutTimer?.Dispose();

                // Cancel any ongoing operations
                if (!this.cancellationTokenSource.IsCancellationRequested)
                {
                    this.cancellationTokenSource.Cancel();
                }
                this.cancellationTokenSource.Dispose();

                // Handle auto-rollback if configured and transaction is still active
                // Note: Synchronous dispose cannot safely await async operations
                // Use DisposeAsync() for proper async disposal with rollback
                if (this.options.AutoRollbackOnDispose && this.State == TransactionState.Active)
                {
                    this.logger.LogWarning("Auto-rollback on dispose detected in synchronous Dispose() for transaction {TransactionId}. " +
                        "Consider using DisposeAsync() for proper async disposal. Skipping rollback to avoid deadlocks.", this.TransactionId);
                    
                    lock (this.stateLock)
                    {
                        this.state = TransactionState.Failed;
                    }
                }

                // Clear collections to prevent memory leaks
                this.savepoints.Clear();
                lock (this.completionCallbacks)
                {
                    this.completionCallbacks.Clear();
                }
            }

            this.disposed = true;
        }

        // Destructor (finalizer)
        ~TransactionCoordinator()
        {
            Dispose(false);
        }
    }
}