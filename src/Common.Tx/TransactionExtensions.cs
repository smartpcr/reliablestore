//-------------------------------------------------------------------------------
// <copyright file="TransactionExtensions.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.DependencyInjection;
    using Unity;
    using Unity.Lifetime;

    /// <summary>
    /// Extension methods for transaction integration and error handling
    /// </summary>
    public static class TransactionExtensions
    {
        /// <summary>
        /// Register basic transaction services with Unity container
        /// For full URP integration, use UnityTransactionConfiguration.RegisterUrpTransactionServices()
        /// </summary>
        public static IUnityContainer AddTransactionSupport(this IUnityContainer container)
        {
            // Register transaction factory
            container.RegisterType<ITransactionFactory, TransactionFactory>(new ContainerControlledLifetimeManager());

            // Register transaction-aware repository and cache factories
            container.RegisterType<ITransactionalRepositoryFactory, TransactionalRepositoryFactory>(new ContainerControlledLifetimeManager());

            return container;
        }

        /// <summary>
        /// Register transaction services with Microsoft.Extensions.DependencyInjection
        /// </summary>
        public static IServiceCollection AddTransactionSupport(this IServiceCollection services)
        {
            services.AddSingleton<ITransactionFactory, TransactionFactory>();
            services.AddSingleton<ITransactionalRepositoryFactory, TransactionalRepositoryFactory>();

            return services;
        }

        /// <summary>
        /// Execute action with automatic transaction management and rollback on failure
        /// </summary>
        public static async Task ExecuteInTransactionAsync(this ITransactionFactory factory,
            Func<ITransaction, Task> action,
            TransactionOptions options = null,
            CancellationToken cancellationToken = default)
        {
            using var transaction = factory.CreateTransaction(options);
            var previousTransaction = TransactionContext.Current;
            TransactionContext.Current = transaction;

            try
            {
                await action(transaction);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                catch (Exception rollbackEx)
                {
                    // Log rollback failure but throw original exception
                    // Rollback exceptions are typically logged but don't mask the original failure
                    throw new AggregateException("Transaction failed and rollback also failed", ex, rollbackEx);
                }
                throw;
            }
            finally
            {
                TransactionContext.Current = previousTransaction;
            }
        }

        /// <summary>
        /// Execute function with automatic transaction management and rollback on failure
        /// </summary>
        public static async Task<T> ExecuteInTransactionAsync<T>(this ITransactionFactory factory,
            Func<ITransaction, Task<T>> func,
            TransactionOptions options = null,
            CancellationToken cancellationToken = default)
        {
            using var transaction = factory.CreateTransaction(options);
            var previousTransaction = TransactionContext.Current;
            TransactionContext.Current = transaction;

            try
            {
                var result = await func(transaction);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception ex)
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                catch (Exception rollbackEx)
                {
                    throw new AggregateException("Transaction failed and rollback also failed", ex, rollbackEx);
                }
                throw;
            }
            finally
            {
                TransactionContext.Current = previousTransaction;
            }
        }

        /// <summary>
        /// Execute action with retry logic and transaction management
        /// </summary>
        public static async Task ExecuteWithRetryAsync(this ITransactionFactory factory,
            Func<ITransaction, Task> action,
            int maxRetries = 3,
            TimeSpan? retryDelay = null,
            TransactionOptions options = null,
            CancellationToken cancellationToken = default)
        {
            retryDelay ??= TimeSpan.FromMilliseconds(500);
            var attempts = 0;
            Exception lastException = null;

            while (attempts <= maxRetries)
            {
                try
                {
                    await factory.ExecuteInTransactionAsync(action, options, cancellationToken);
                    return; // Success
                }
                catch (Exception ex) when (IsRetryableException(ex) && attempts < maxRetries)
                {
                    lastException = ex;
                    attempts++;

                    var delay = TimeSpan.FromMilliseconds(retryDelay.Value.TotalMilliseconds * Math.Pow(2, attempts - 1));
                    await Task.Delay(delay, cancellationToken);
                }
            }

            throw lastException ?? new InvalidOperationException("Retry logic failed unexpectedly");
        }

        /// <summary>
        /// Execute function with retry logic and transaction management
        /// </summary>
        public static async Task<T> ExecuteWithRetryAsync<T>(this ITransactionFactory factory,
            Func<ITransaction, Task<T>> func,
            int maxRetries = 3,
            TimeSpan? retryDelay = null,
            TransactionOptions options = null,
            CancellationToken cancellationToken = default)
        {
            retryDelay ??= TimeSpan.FromMilliseconds(500);
            var attempts = 0;
            Exception lastException = null;

            while (attempts <= maxRetries)
            {
                try
                {
                    return await factory.ExecuteInTransactionAsync(func, options, cancellationToken);
                }
                catch (Exception ex) when (IsRetryableException(ex) && attempts < maxRetries)
                {
                    lastException = ex;
                    attempts++;

                    var delay = TimeSpan.FromMilliseconds(retryDelay.Value.TotalMilliseconds * Math.Pow(2, attempts - 1));
                    await Task.Delay(delay, cancellationToken);
                }
            }

            throw lastException ?? new InvalidOperationException("Retry logic failed unexpectedly");
        }

        /// <summary>
        /// Create savepoint with automatic cleanup
        /// </summary>
        public static async Task<IDisposable> CreateSavepointScopeAsync(this ITransaction transaction,
            string name,
            CancellationToken cancellationToken = default)
        {
            var savepoint = await transaction.CreateSavepointAsync(name, cancellationToken);
            return new SavepointScope(transaction, savepoint);
        }

        /// <summary>
        /// Enlist repository in current transaction if one exists
        /// </summary>
        public static TransactionalRepository<TData> AsTransactional<TData>(
            this IRepository<TData> repository,
            ITransactionalRepositoryFactory factory) where TData : class
        {
            // Factory is now responsible for creation and enlistment
            return factory.CreateTransactionalRepository(repository);
        }

        private static bool IsRetryableException(Exception ex)
        {
            // Define which exceptions are retryable
            return ex is TimeoutException ||
                   ex is TaskCanceledException ||
                   ex is OperationCanceledException ||
                   (ex is TransactionException txEx && ex.InnerException != null && IsRetryableException(ex.InnerException));
        }
    }
}