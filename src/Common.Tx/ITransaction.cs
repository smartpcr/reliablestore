//-------------------------------------------------------------------------------
// <copyright file="ITransaction.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Core transaction interface providing ACID semantics
    /// </summary>
    public interface ITransaction : IDisposable
    {
        /// <summary>
        /// Unique transaction identifier
        /// </summary>
        string TransactionId { get; }

        /// <summary>
        /// Current transaction state
        /// </summary>
        TransactionState State { get; }

        /// <summary>
        /// Transaction isolation level
        /// </summary>
        IsolationLevel IsolationLevel { get; }

        /// <summary>
        /// Transaction timeout
        /// </summary>
        TimeSpan Timeout { get; }

        /// <summary>
        /// Commit all changes made in this transaction
        /// </summary>
        Task CommitAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Rollback all changes made in this transaction
        /// </summary>
        Task RollbackAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Create a savepoint for partial rollback
        /// </summary>
        Task<ISavepoint> CreateSavepointAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>
        /// Rollback to a specific savepoint
        /// </summary>
        Task RollbackToSavepointAsync(ISavepoint savepoint, CancellationToken cancellationToken = default);

        /// <summary>
        /// Enlist a resource in this transaction
        /// </summary>
        void EnlistResource(ITransactionalResource resource);

        /// <summary>
        /// Get enlisted resources by type
        /// </summary>
        IEnumerable<T> GetEnlistedResources<T>() where T : ITransactionalResource;

        /// <summary>
        /// Add a completion callback
        /// </summary>
        void AddCompletionCallback(Func<TransactionState, Task> callback);
    }
}