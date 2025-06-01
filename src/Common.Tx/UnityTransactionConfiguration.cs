//-------------------------------------------------------------------------------
// <copyright file="UnityTransactionConfiguration.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    using System;
    using Microsoft.Extensions.Logging;
    using Unity;
    using Unity.Lifetime;

    /// <summary>
    /// Unity container configuration extensions for transaction services
    /// </summary>
    public static class UnityTransactionConfiguration
    {
        /// <summary>
        /// Register core transaction services with Unity container
        /// </summary>
        public static IUnityContainer RegisterTransactionServices(this IUnityContainer container)
        {
            // Register core transaction services
            container.RegisterType<ITransactionFactory, TransactionFactory>(new ContainerControlledLifetimeManager());

            // Register transaction factory with defaults
            container.RegisterFactory<ITransactionFactory>("DefaultTransactionFactory", (c) =>
            {
                var loggerFactory = c.Resolve<ILoggerFactory>();
                return new TransactionFactoryWithDefaults(loggerFactory);
            });

            // Register other services
            container.RegisterType<ITransactionalRepositoryFactory, TransactionalRepositoryFactory>(new ContainerControlledLifetimeManager());

            return container;
        }

        /// <summary>
        /// Register transaction services with custom options
        /// </summary>
        public static IUnityContainer RegisterTransactionServices(this IUnityContainer container, TransactionOptions defaultOptions)
        {
            // Register core transaction services
            container.RegisterTransactionServices();

            // Register custom transaction options
            container.RegisterInstance("DefaultTransactionOptions", defaultOptions);

            // Register transaction factory with custom defaults
            container.RegisterFactory<ITransactionFactory>("CustomTransactionFactory", (c) =>
            {
                var loggerFactory = c.Resolve<ILoggerFactory>();
                return new TransactionFactoryWithDefaults(loggerFactory, defaultOptions);
            });

            return container;
        }
    }

    /// <summary>
    /// Transaction factory with default options
    /// </summary>
    internal class TransactionFactoryWithDefaults : ITransactionFactory
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly TransactionOptions _defaultOptions;

        public TransactionFactoryWithDefaults(ILoggerFactory loggerFactory, TransactionOptions defaultOptions = null)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _defaultOptions = defaultOptions ?? new TransactionOptions();
        }

        public ITransaction CreateTransaction(TransactionOptions options = null)
        {
            var effectiveOptions = options ?? _defaultOptions;
            var logger = _loggerFactory.CreateLogger<TransactionCoordinator>();
            return new TransactionCoordinator(logger, effectiveOptions);
        }

        public ITransaction Current => TransactionContext.Current;
    }
}