//-------------------------------------------------------------------------------
// <copyright file="ITransactionFactory.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    /// <summary>
    /// Transaction factory interface
    /// </summary>
    public interface ITransactionFactory
    {
        /// <summary>
        /// Create a new transaction
        /// </summary>
        ITransaction CreateTransaction(TransactionOptions options = null);

        /// <summary>
        /// Get current ambient transaction
        /// </summary>
        ITransaction Current { get; }
    }
}