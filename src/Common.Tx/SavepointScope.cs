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
        private readonly ITransaction _transaction;
        private readonly ISavepoint _savepoint;
        private bool _disposed;

        public SavepointScope(ITransaction transaction, ISavepoint savepoint)
        {
            _transaction = transaction;
            _savepoint = savepoint;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Savepoints are automatically cleaned up by the transaction
                // This is mainly for future extensibility
                _disposed = true;
            }
        }
    }
}