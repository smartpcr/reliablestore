//-------------------------------------------------------------------------------
// <copyright file="TransactionContext.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Transaction context for ambient transaction pattern
    /// </summary>
    public static class TransactionContext
    {
        private static readonly AsyncLocal<ITransaction> _current = new AsyncLocal<ITransaction>();

        /// <summary>
        /// Current ambient transaction
        /// </summary>
        public static ITransaction Current
        {
            get => _current.Value;
            internal set => _current.Value = value;
        }

        /// <summary>
        /// Execute action within transaction scope
        /// </summary>
        public static async Task ExecuteInTransactionAsync(ITransactionFactory factory, Func<ITransaction, Task> action, TransactionOptions options = null)
        {
            using var transaction = factory.CreateTransaction(options);
            var previousTransaction = Current;
            Current = transaction;

            try
            {
                await action(transaction);
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
            finally
            {
                Current = previousTransaction;
            }
        }

        /// <summary>
        /// Execute function within transaction scope with return value
        /// </summary>
        public static async Task<T> ExecuteInTransactionAsync<T>(ITransactionFactory factory, Func<ITransaction, Task<T>> func, TransactionOptions options = null)
        {
            using var transaction = factory.CreateTransaction(options);
            var previousTransaction = Current;
            Current = transaction;

            try
            {
                var result = await func(transaction);
                await transaction.CommitAsync();
                return result;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
            finally
            {
                Current = previousTransaction;
            }
        }
    }
}