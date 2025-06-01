//-------------------------------------------------------------------------------
// <copyright file="ITransactionalResource.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Interface for resources that participate in transactions
    /// </summary>
    public interface ITransactionalResource
    {
        /// <summary>
        /// Resource identifier
        /// </summary>
        string ResourceId { get; }

        /// <summary>
        /// Prepare for transaction commit (2-phase commit prepare phase)
        /// </summary>
        Task<bool> PrepareAsync(ITransaction transaction, CancellationToken cancellationToken = default);

        /// <summary>
        /// Commit changes (2-phase commit commit phase)
        /// </summary>
        Task CommitAsync(ITransaction transaction, CancellationToken cancellationToken = default);

        /// <summary>
        /// Rollback changes
        /// </summary>
        Task RollbackAsync(ITransaction transaction, CancellationToken cancellationToken = default);

        /// <summary>
        /// Create a persistent marker for the current state of the resource within this transaction.
        /// </summary>
        Task CreateSavepointAsync(ITransaction transaction, ISavepoint savepoint, CancellationToken cancellationToken = default);

        /// <summary>
        /// Rollback to savepoint
        /// </summary>
        Task RollbackToSavepointAsync(ITransaction transaction, ISavepoint savepoint, CancellationToken cancellationToken = default);

        /// <summary>
        /// Discard any persisted state associated with the given savepoint for this resource.
        /// </summary>
        Task DiscardSavepointDataAsync(ITransaction transaction, ISavepoint savepointToDiscard, CancellationToken cancellationToken = default);
    }
}