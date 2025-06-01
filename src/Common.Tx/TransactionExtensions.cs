//-------------------------------------------------------------------------------
// <copyright file="TransactionExtensions.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    using System;
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
        // For .NET 6+ Random.Shared would be preferred. For broader compatibility, a static instance is used.
        private static readonly Random _jitterer = new Random();

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
            await using var transaction = factory.CreateTransaction(options);
            var previousTransaction = TransactionContext.Current;

            try
            {
                TransactionContext.Current = transaction;
                await action(transaction).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception rollbackEx)
                {
                    // Consider logging both ex and rollbackEx here using a proper logging framework.
                    // e.g., logger.LogError(rollbackEx, "Transaction rollback failed after an initial operation failure. Original exception: {OriginalExceptionType}", ex.GetType().Name);
                    throw new AggregateException("Transaction failed and rollback also failed. See inner exceptions for details.", ex, rollbackEx);
                }
                throw; // Rethrow the original exception if rollback succeeded
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
            await using var transaction = factory.CreateTransaction(options);
            var previousTransaction = TransactionContext.Current;

            try
            {
                TransactionContext.Current = transaction;
                var result = await func(transaction).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (Exception ex)
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception rollbackEx)
                {
                    // Consider logging both ex and rollbackEx here.
                    throw new AggregateException("Transaction failed and rollback also failed. See inner exceptions for details.", ex, rollbackEx);
                }
                throw; // Rethrow the original exception if rollback succeeded
            }
            finally
            {
                TransactionContext.Current = previousTransaction;
            }
        }

        /// <summary>
        /// Execute action with retry logic and transaction management.
        /// </summary>
        /// <param name="factory">The transaction factory.</param>
        /// <param name="action">The action to execute within a transaction.</param>
        /// <param name="maxRetries">The total number of attempts to make. Must be at least 1.</param>
        /// <param name="retryDelay">The base delay between retries. This delay will be exponentially backed off with jitter.</param>
        /// <param name="options">Transaction options.</param>
        /// <param name="cancellationToken">A cancellation token to observe.</param>
        public static async Task ExecuteWithRetryAsync(this ITransactionFactory factory,
            Func<ITransaction, Task> action,
            int maxRetries = 3,
            TimeSpan? retryDelay = null,
            TransactionOptions options = null,
            CancellationToken cancellationToken = default)
        {
            if (maxRetries < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRetries), "Number of attempts must be at least 1.");
            }

            retryDelay ??= TimeSpan.FromMilliseconds(500);
            var attempts = 0;

            while (attempts < maxRetries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await factory.ExecuteInTransactionAsync(action, options, cancellationToken).ConfigureAwait(false);
                    return; // Success
                }
                catch (Exception ex) when (IsRetryableException(ex, cancellationToken) && attempts < maxRetries - 1)
                {
                    attempts++;

                    double baseDelayMs = retryDelay.Value.TotalMilliseconds * Math.Pow(2, attempts - 1);
                    // Add jitter: e.g., +/- 10% of the current delay component.
                    // Ensures the jitter amount is at least 1ms if baseDelayMs is very small, to have some effect.
                    double jitterMagnitude = Math.Max(1.0, baseDelayMs * 0.1);
                    double jitterMs = (_jitterer.NextDouble() * 2.0 * jitterMagnitude) - jitterMagnitude; // Random number between -jitterMagnitude and +jitterMagnitude

                    double finalDelayMs = Math.Max(0, baseDelayMs + jitterMs); // Ensure non-negative delay

                    await Task.Delay(TimeSpan.FromMilliseconds(finalDelayMs), cancellationToken).ConfigureAwait(false);
                }
                // If the exception was not caught by the 'when' clause (non-retryable or last attempt),
                // it will propagate out of this try-catch, then out of the while loop, and be thrown by the caller.
            }
            // If the loop completes, it means all retries were attempted and the last one failed (its exception propagated),
            // or success occurred and returned. No code needed here for the non-generic Task version.
        }

        /// <summary>
        /// Execute function with retry logic and transaction management.
        /// </summary>
        /// <typeparam name="T">The return type of the function.</typeparam>
        /// <param name="factory">The transaction factory.</param>
        /// <param name="func">The function to execute within a transaction.</param>
        /// <param name="maxRetries">The total number of attempts to make. Must be at least 1.</param>
        /// <param name="retryDelay">The base delay between retries. This delay will be exponentially backed off with jitter.</param>
        /// <param name="options">Transaction options.</param>
        /// <param name="cancellationToken">A cancellation token to observe.</param>
        /// <returns>The result of the function.</returns>
        public static async Task<T> ExecuteWithRetryAsync<T>(this ITransactionFactory factory,
            Func<ITransaction, Task<T>> func,
            int maxRetries = 3,
            TimeSpan? retryDelay = null,
            TransactionOptions options = null,
            CancellationToken cancellationToken = default)
        {
            if (maxRetries < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRetries), "Number of attempts must be at least 1.");
            }

            retryDelay ??= TimeSpan.FromMilliseconds(500);
            var attempts = 0;

            while (attempts < maxRetries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await factory.ExecuteInTransactionAsync(func, options, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsRetryableException(ex, cancellationToken) && attempts < maxRetries - 1)
                {
                    attempts++;

                    double baseDelayMs = retryDelay.Value.TotalMilliseconds * Math.Pow(2, attempts - 1);
                    double jitterMagnitude = Math.Max(1.0, baseDelayMs * 0.1);
                    double jitterMs = (_jitterer.NextDouble() * 2.0 * jitterMagnitude) - jitterMagnitude;
                    double finalDelayMs = Math.Max(0, baseDelayMs + jitterMs);

                    await Task.Delay(TimeSpan.FromMilliseconds(finalDelayMs), cancellationToken).ConfigureAwait(false);
                }
            }
            // This line is for compiler satisfaction, as all code paths in a Task<T>-returning method must return or throw.
            // Logically, it should be unreachable if maxRetries >= 1, as the loop either returns on success
            // or the exception from the final attempt propagates out.
            throw new InvalidOperationException("All retry attempts failed. This indicates an unexpected state or flaw in retry logic if reached.");
        }

        /// <summary>
        /// Create savepoint with automatic rollback on dispose
        /// </summary>
        public static async Task<SavepointScope> CreateSavepointScopeAsync(this ITransaction transaction,
            string name,
            CancellationToken cancellationToken = default)
        {
            var savepoint = await transaction.CreateSavepointAsync(name, cancellationToken).ConfigureAwait(false);
            return new SavepointScope(transaction, savepoint);
        }

        /// <summary>
        /// Enlist repository in current transaction if one exists
        /// </summary>
        public static TransactionalRepository<TData> AsTransactional<TData>(
            this IRepository<TData> repository,
            ITransactionalRepositoryFactory factory) where TData : class
        {
            return factory.CreateTransactionalRepository(repository);
        }

        private static bool IsRetryableException(Exception ex, CancellationToken operationToken = default)
        {
            OperationCanceledException oceToTest = null;
            if (ex is OperationCanceledException oce)
            {
                oceToTest = oce;
            }
            else if (ex is TaskCanceledException tce && tce.InnerException is OperationCanceledException tceInnerOce)
            {
                oceToTest = tceInnerOce;
            }

            if (oceToTest != null)
            {
                if (operationToken.CanBeCanceled && oceToTest.CancellationToken == operationToken && operationToken.IsCancellationRequested)
                {
                    return false; // Not retryable if due to the operation's own token being cancelled.
                }
            }

            // General retryable conditions
            if (ex is TimeoutException || ex is TransactionTimeoutException)
                return true;

            // These cover OperationCanceledExceptions/TaskCanceledExceptions not caught by the specific operationToken check.
            // e.g., an internal operation timing out and cancelling itself via a different CancellationToken.
            if (ex is TaskCanceledException || ex is OperationCanceledException)
            {
                 // If it's an OCE not from our operationToken, or a TCE not wrapping such an OCE, consider it retryable.
                return true;
            }

            if (ex is TransactionException txEx)
            {
                return txEx.TransactionState == TransactionState.Timeout ||
                       (txEx.InnerException != null && IsRetryableException(txEx.InnerException, operationToken));
            }

            return false; // Default to not retryable
        }
    }
}