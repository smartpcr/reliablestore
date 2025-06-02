// -----------------------------------------------------------------------
// <copyright file="UnityConfig.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Factory
{
    using Common.Persistence.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Unity;
    using Unity.Injection;

    public static class DependencyInjection
    {
        public static IUnityContainer AddPersistence(this IUnityContainer container)
        {
            container.RegisterType<IConfigReader, JsonConfigReader>();
            container.RegisterType<ICrudStorageProviderFactory, CrudStorageProviderFactory>(
                new InjectionConstructor(
                    new DIContainerWrapper(container),
                    container.Resolve<IConfigReader>()));
            container.RegisterType<IIndexingProviderFactory, IndexingProviderFactory>(
                new InjectionConstructor(
                    new DIContainerWrapper(container),
                    container.Resolve<IConfigReader>()));
            container.RegisterType<IArchivalProviderFactory, ArchivalProviderFactory>(
                new InjectionConstructor(
                    new DIContainerWrapper(container),
                    container.Resolve<IConfigReader>()));
            container.RegisterType<IPurgeProviderFactory, PurgeProviderFactory>(
                new InjectionConstructor(
                    new DIContainerWrapper(container),
                    container.Resolve<IConfigReader>()));
            container.RegisterType<IBackupProviderFactory, BackupProviderFactory>(
                new InjectionConstructor(
                    new DIContainerWrapper(container),
                    container.Resolve<IConfigReader>()));
            container.RegisterType<IMigrationProviderFactory, MigrationProviderFactory>(
                new InjectionConstructor(
                    new DIContainerWrapper(container),
                    container.Resolve<IConfigReader>()));
            container.RegisterType<ISerializerFactory, SerializerFactory>(
                new InjectionConstructor(
                    new DIContainerWrapper(container),
                    container.Resolve<IConfigReader>()));

            return container;
        }

        public static IServiceCollection AddPersistence(this IServiceCollection services)
        {
            IConfigReader configReader = new JsonConfigReader();
            services.AddSingleton<IConfigReader>(configReader);
            services.AddSingleton<ICrudStorageProviderFactory>(new CrudStorageProviderFactory(
                new DIContainerWrapper(services), configReader));
            services.AddSingleton<IIndexingProviderFactory>(new IndexingProviderFactory(
                new DIContainerWrapper(services), configReader));
            services.AddSingleton<IArchivalProviderFactory>(new ArchivalProviderFactory(
                new DIContainerWrapper(services), configReader));
            services.AddSingleton<IPurgeProviderFactory>(new PurgeProviderFactory(
                new DIContainerWrapper(services), configReader));
            services.AddSingleton<IBackupProviderFactory>(new BackupProviderFactory(
                new DIContainerWrapper(services), configReader));
            services.AddSingleton<IMigrationProviderFactory>(new MigrationProviderFactory(
                new DIContainerWrapper(services), configReader));
            services.AddSingleton<ISerializerFactory>(new SerializerFactory(
                new DIContainerWrapper(services), configReader));

            return services;
        }
    }
}