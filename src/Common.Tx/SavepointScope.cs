//-------------------------------------------------------------------------------
// <copyright file="SavepointScope.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// Savepoint scope for automatic rollback on dispose
    /// </summary>
    public class SavepointScope : IDisposable, IAsyncDisposable
    {
        private readonly ITransaction transaction;
        private readonly ISavepoint savepoint;
        private bool _disposed;
        private bool _committed;

        public SavepointScope(ITransaction transaction, ISavepoint savepoint)
        {
            this.transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
            this.savepoint = savepoint ?? throw new ArgumentNullException(nameof(savepoint));
        }

        /// <summary>
        /// Commit the savepoint (prevents rollback on dispose)
        /// </summary>
        public void Commit()
        {
            ThrowIfDisposed();
            _committed = true;
        }

        public void Dispose()
        {
            if (!_disposed && !_committed)
            {
                // Synchronous dispose cannot safely await async operations
                // For proper rollback-to-savepoint functionality, use DisposeAsync
                _disposed = true;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed && !_committed)
            {
                try
                {
                    await transaction.RollbackToSavepointAsync(savepoint);
                }
                catch
                {
                    // Rollback failures during dispose should not throw
                    // They're typically logged by the transaction implementation
                }
            }
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SavepointScope));
            }
        }
    }
}