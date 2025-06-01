//-------------------------------------------------------------------------------
// <copyright file="TransactionFactory.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Default implementation of transaction factory
    /// </summary>
    public class TransactionFactory : ITransactionFactory
    {
        private readonly ILoggerFactory loggerFactory;

        public TransactionFactory(ILoggerFactory loggerFactory)
        {
            this.loggerFactory = loggerFactory;
        }

        public ITransaction CreateTransaction(TransactionOptions options = null)
        {
            var logger = this.loggerFactory.CreateLogger<TransactionCoordinator>();
            return new TransactionCoordinator(logger, options);
        }

        public ITransaction Current => TransactionContext.Current;
    }
}