//-------------------------------------------------------------------------------
// <copyright file="TransactionServiceCollectionExtensions.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Transaction-aware service collection extensions
    /// </summary>
    public static class TransactionServiceCollectionExtensions
    {
        /// <summary>
        /// Add a transactional repository to the service collection
        /// </summary>
        public static IServiceCollection AddTransactionalRepository<T, TImpl>(this IServiceCollection services)
            where T : class, IRepository<object>
            where TImpl : class, T
        {
            services.AddSingleton<T, TImpl>();
            services.AddTransient<TransactionalRepository<object>>(sp =>
            {
                var repository = sp.GetRequiredService<T>();
                var logger = sp.GetRequiredService<ILogger<TransactionalRepository<object>>>();
                return new TransactionalRepository<object>((IRepository<object>)repository, logger);
            });

            return services;
        }
    }
}