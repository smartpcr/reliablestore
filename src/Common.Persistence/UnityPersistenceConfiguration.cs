//-------------------------------------------------------------------------------
// <copyright file="UnityPersistenceConfiguration.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

#if UNITY_CONTAINER
namespace Common.Persistence
{
    using System;
    using Microsoft.Extensions.Logging;
    using Unity;
    using Unity.Lifetime;

    /// <summary>
    /// Unity container configuration extensions for persistence services
    /// Note: This is persistence layer only - no transaction awareness
    /// </summary>
    public static class UnityPersistenceConfiguration
    {
        /// <summary>
        /// Register core persistence services with Unity container
        /// </summary>
        public static IUnityContainer RegisterPersistenceServices(this IUnityContainer container)
        {
            // Register the FileStore as a singleton
            container.RegisterType(typeof(FileStore<>), new ContainerControlledLifetimeManager());

            return container;
        }

        /// <summary>
        /// Register persistence services for a specific entity type
        /// </summary>
        public static IUnityContainer RegisterFileStore<T>(this IUnityContainer container, string filePath)
            where T : class
        {
            container.RegisterFactory<FileStore<T>>(c =>
            {
                var logger = c.Resolve<ILogger<FileStore<T>>>();
                return new FileStore<T>(filePath, logger);
            });

            return container;
        }
    }
}
#endif