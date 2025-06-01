//-------------------------------------------------------------------------------
// <copyright file="TransactionalRepositoryFactory.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    using System;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Implementation of transactional repository factory
    /// </summary>
    public class TransactionalRepositoryFactory : ITransactionalRepositoryFactory
    {
        private readonly ILoggerFactory _loggerFactory;

        public TransactionalRepositoryFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        public TransactionalRepository<T> CreateTransactionalRepository<T>(IRepository<T> underlyingRepository) where T : class
        {
            var logger = _loggerFactory.CreateLogger<TransactionalRepository<T>>();
            var transaction = TransactionContext.Current;

            if (transaction == null)
            {
                throw new InvalidOperationException("Cannot create transactional repository: no ambient transaction found. Ensure operations are within a transaction scope (e.g., using TransactionContext.ExecuteInTransactionAsync).");
            }

            var transactionalRepo = new TransactionalRepository<T>(underlyingRepository, logger);
            transaction.EnlistResource(transactionalRepo);
            return transactionalRepo;
        }
    }
}