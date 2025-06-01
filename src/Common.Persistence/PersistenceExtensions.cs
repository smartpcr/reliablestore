//-------------------------------------------------------------------------------
// <copyright file="PersistenceExtensions.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence
{
    using System;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Extension methods for registering persistence services with dependency injection containers
    /// </summary>
    public static class PersistenceExtensions
    {
        /// <summary>
        /// Register persistence services with Microsoft.Extensions.DependencyInjection
        /// Note: This is persistence layer only - no transaction awareness
        /// </summary>
        public static IServiceCollection AddPersistenceSupport(this IServiceCollection services)
        {
            // Register the FileStore as a transient service - this is persistence layer only
            services.AddTransient(typeof(FileStore<>));

            return services;
        }

        /// <summary>
        /// Register persistence services for a specific entity type
        /// </summary>
        public static IServiceCollection AddFileStore<T>(this IServiceCollection services, string filePath)
            where T : class
        {
            services.AddTransient<FileStore<T>>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<FileStore<T>>>();
                return new FileStore<T>(filePath, logger);
            });

            return services;
        }
    }
}