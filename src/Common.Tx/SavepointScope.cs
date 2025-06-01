//-------------------------------------------------------------------------------
// <copyright file="SavepointScope.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    using System;

    /// <summary>
    /// Savepoint scope for automatic cleanup
    /// </summary>
    internal class SavepointScope : IDisposable
    {
        private readonly ITransaction transaction;
        private readonly ISavepoint savepoint;
        private bool _disposed;

        public SavepointScope(ITransaction transaction, ISavepoint savepoint)
        {
            this.transaction = transaction;
            this.savepoint = savepoint;
        }

        public void Dispose()
        {
            if (!this._disposed)
            {
                // Savepoints are automatically cleaned up by the transaction
                // This is mainly for future extensibility
                this._disposed = true;
            }
        }
    }
}