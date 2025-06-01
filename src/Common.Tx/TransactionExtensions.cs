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
                    // It's crucial to log both the original exception (ex) and the rollbackEx here.
                    // For example, using an ILogger:
                    // logger.LogError(rollbackEx, "Transaction rollback failed after an initial operation failure. Original exception: {OriginalExceptionType}", ex.GetType().Name);
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
                    // It's crucial to log both the original exception (ex) and the rollbackEx here.
                    // For example, using an ILogger:
                    // logger.LogError(rollbackEx, "Transaction rollback failed after an initial operation failure. Original exception: {OriginalExceptionType}", ex.GetType().Name);
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
        /// <param name="retryDelay">The base delay between retries. This delay will be exponentially backed off.</param>
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
            // Exception lastException = null; // No longer needed here as the final throw is removed

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
                    // lastException = ex; // Store if needed for logging, but not for the final throw
                    attempts++;

                    var delay = TimeSpan.FromMilliseconds(retryDelay.Value.TotalMilliseconds * Math.Pow(2, attempts - 1));
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                // If the exception was not caught by the 'when' clause (non-retryable or last attempt),
                // it will propagate out of this try-catch, then out of the while loop, and be thrown by the caller.
                // This is the desired behavior for the final attempt's failure.
            }
            // If the loop completes, it means all retries were attempted and the last one failed,
            // and its exception has already propagated. Or, success occurred and returned.
            // Thus, this point should not be reached if maxRetries >= 1 and an exception occurred on the last attempt.
            // The compiler ensures all paths return for Task<T>; for Task, an unhandled path would mean implicit completion.
            // The logic above ensures an exception from the final attempt propagates, or success returns.
        }

        /// <summary>
        /// Execute function with retry logic and transaction management.
        /// </summary>
        /// <typeparam name="T">The return type of the function.</typeparam>
        /// <param name="factory">The transaction factory.</param>
        /// <param name="func">The function to execute within a transaction.</param>
        /// <param name="maxRetries">The total number of attempts to make. Must be at least 1.</param>
        /// <param name="retryDelay">The base delay between retries. This delay will be exponentially backed off.</param>
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
            // Exception lastException = null; // No longer needed here

            while (attempts < maxRetries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await factory.ExecuteInTransactionAsync(func, options, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsRetryableException(ex, cancellationToken) && attempts < maxRetries - 1)
                {
                    // lastException = ex; // Store if needed for logging
                    attempts++;

                    var delay = TimeSpan.FromMilliseconds(retryDelay.Value.TotalMilliseconds * Math.Pow(2, attempts - 1));
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
            // As with the non-generic version, if the loop finishes, the exception from the
            // final attempt has propagated, or success returned.
            // For Task<T>, the compiler enforces that all paths must return a value or throw.
            // The structure ensures this. If this line were reachable, it would be a compiler error
            // for Task<T> unless it threw or returned.
            // Given the logic, it's not expected to be reached if the last attempt fails.
            // If it were, `throw new InvalidOperationException("All retries failed and loop exited unexpectedly.");`
            // might be appropriate, but the current logic should prevent this.
            // The C# compiler will verify that all code paths return a value or throw an exception for methods returning Task<T>.
            // Since the loop either returns on success or the exception from the last attempt propagates,
            // this point is effectively unreachable.
            throw new InvalidOperationException("All retry attempts failed. This line should be unreachable if logic is correct."); // Should be optimized away or indicate a logic flaw if hit.
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
            // Factory is now responsible for creation and enlistment
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
                // If TaskCanceledException wraps an OperationCanceledException, check the inner one.
                oceToTest = tceInnerOce;
            }

            if (oceToTest != null)
            {
                // If the cancellation is due to the operation's own token, it's not retryable.
                if (operationToken.CanBeCanceled && oceToTest.CancellationToken == operationToken && operationToken.IsCancellationRequested)
                {
                    return false;
                }
                // If oceToTest.CancellationToken is not our operationToken, or if operationToken was not cancelled,
                // then this specific OperationCanceledException might be from another source (e.g. an internal timeout that self-cancels)
                // and could be considered retryable by the general checks below.
            }

            // General retryable conditions
            if (ex is TimeoutException ||
                ex is TransactionTimeoutException)
                return true;

            // These checks cover OperationCanceledExceptions/TaskCanceledExceptions not caught by the specific operationToken check above.
            // For example, an internal operation timing out and cancelling itself via a different CancellationToken.
            if (ex is TaskCanceledException || ex is OperationCanceledException)
            {
                return true;
            }

            if (ex is TransactionException txEx)
            {
                return txEx.TransactionState == TransactionState.Timeout ||
                       (txEx.InnerException != null && IsRetryableException(txEx.InnerException, operationToken)); // Recursive call
            }

            return false; // Default to not retryable
        }
    }
}