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
            Exception lastException = null;

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
                    lastException = ex;
                    attempts++;

                    // Exponential backoff: delay * 2^(attempts-1)
                    // For attempts = 1 (first retry), multiplier is 2^0 = 1
                    // For attempts = 2 (second retry), multiplier is 2^1 = 2
                    var delay = TimeSpan.FromMilliseconds(retryDelay.Value.TotalMilliseconds * Math.Pow(2, attempts - 1));
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                // If the exception was not caught by the 'when' clause (non-retryable or last attempt),
                // it will propagate out of this try-catch, then out of the while loop, and be thrown by the caller.
                // If it's the last attempt and it fails, the exception from ExecuteInTransactionAsync will be thrown directly.
            }

            // This part is reached if all retries failed with a retryable exception.
            // The exception from the final attempt would have been thrown directly by ExecuteInTransactionAsync
            // if it was non-retryable or if it was the last attempt.
            // If the loop completes due to maxRetries being exhausted with retryable exceptions,
            // throw the last recorded exception.
            // However, the current structure means the exception from the last attempt's ExecuteInTransactionAsync
            // will be the one that propagates if it's not caught by the 'when' clause.
            // This final throw is a safeguard or for clarity if the loop condition was different.
            // Given the current loop and catch-when, if all attempts fail, the exception from the *last* ExecuteInTransactionAsync call
            // will be the one that's thrown, not necessarily 'lastException' from a *previous* retryable attempt.
            // To ensure the *very last* exception is thrown:
            // The exception from the final attempt (attempts == maxRetries - 1) will not enter the 'when' block's
            // "attempts < maxRetries - 1" condition, so it will propagate out.
            // Thus, this explicit throw below is technically only hit if maxRetries was 1 and it failed retryably (which is not possible with current IsRetryableException logic for the first attempt).
            // Or if maxRetries is 0 (which is now guarded).
            // Let's simplify: the exception from the last attempt will naturally propagate.
            // The only scenario for `lastException` to be non-null and this line to be reached is if
            // the loop condition was different. With `attempts < maxRetries`, the last attempt's exception
            // will directly exit the loop.
            // So, if the loop finishes, it means all attempts (up to maxRetries-1) were caught and retried.
            // The final attempt (attempts = maxRetries-1) happens, and if it throws, that exception propagates.
            // This line is effectively a fallback, but the primary path for failure is the exception from the last ExecuteInTransactionAsync call.
            // If maxRetries is 1, and it fails with a retryable exception, the `when` condition `attempts < maxRetries - 1` (0 < 0) is false, so it throws.
            // If maxRetries is 3:
            // attempt 0: fails retryable, caught, attempts becomes 1, delay.
            // attempt 1: fails retryable, caught, attempts becomes 2, delay.
            // attempt 2: fails (retryable or not), `attempts < maxRetries - 1` (2 < 2) is false. Exception propagates from ExecuteInTransactionAsync.
            // So, lastException here will be from the second-to-last attempt if the last one was also retryable.
            // The exception that should be thrown is the one from the *final* attempt.
            // The current structure correctly throws the exception from the final attempt.
            // This line is a fallback for an unexpected state or if maxRetries is 0 (now handled).
            throw lastException ?? new InvalidOperationException("All retry attempts failed. The final attempt's exception should have been thrown.");
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
            Exception lastException = null;

            while (attempts < maxRetries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await factory.ExecuteInTransactionAsync(func, options, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsRetryableException(ex, cancellationToken) && attempts < maxRetries - 1)
                {
                    lastException = ex;
                    attempts++;

                    var delay = TimeSpan.FromMilliseconds(retryDelay.Value.TotalMilliseconds * Math.Pow(2, attempts - 1));
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                // If the exception was not caught by the 'when' clause (non-retryable or last attempt),
                // it will propagate out of this try-catch, then out of the while loop, and be returned/thrown by the caller.
            }

            // Similar to the non-generic version, the exception from the final attempt will propagate.
            // This is a fallback.
            throw lastException ?? new InvalidOperationException("All retry attempts failed. The final attempt's exception should have been thrown.");
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
            if (ex is OperationCanceledException oce)
            {
                // If the OperationCanceledException's token is the same as the operation's token,
                // and cancellation has been requested for that token, it's not retryable.
                // This check is crucial to respect explicit cancellation requests.
                // Also check if the operationToken is actually cancelable, otherwise oce.CancellationToken might be default(CancellationToken)
                // which would match an uncancelable operationToken.
                if (operationToken.CanBeCanceled && oce.CancellationToken == operationToken && operationToken.IsCancellationRequested)
                {
                    return false;
                }
                // If the OCE is from a different token, an unspecified token, or the operationToken wasn't cancelable,
                // it might represent an internal timeout that could be retried.
            }
            // TaskCanceledException often wraps an OperationCanceledException.
            // If ex is TaskCanceledException and its InnerException is OperationCanceledException,
            // the logic above for OperationCanceledException would apply if we checked ex.InnerException.
            // For simplicity here, we'll treat TaskCanceledException generally, but be mindful it might stem from operationToken.
            // A more robust check might involve inspecting TaskCanceledException.InnerException if it's an OCE.

            return ex is TimeoutException ||
                   ex is TaskCanceledException || // Could be due to operationToken, but often from other sources like HttpClient timeout
                   ex is OperationCanceledException || // Handled above for specific operationToken case
                   ex is TransactionTimeoutException ||
                   (ex is TransactionException txEx &&
                    (txEx.TransactionState == TransactionState.Timeout ||
                     (txEx.InnerException != null && IsRetryableException(txEx.InnerException, operationToken)))); // Recursive call
        }
    }
}