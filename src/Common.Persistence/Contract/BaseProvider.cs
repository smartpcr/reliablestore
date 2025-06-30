// -----------------------------------------------------------------------
// <copyright file="BaseProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Contract
{
    using System;
    using Common.Persistence.Configuration;
    using Common.Persistence.Factory;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Unity;

    public class BaseProvider<T> where T : IEntity
    {
        public UnityContainer? Container { get; }
        protected string Name { get; }
        protected IServiceProvider? ServiceProvider { get; }

        public ProviderCapability Capabilities { get; private set; }
        public ICrudStorageProvider<T> CrudProvider { get; private set; }
        public IIndexingProvider<T> IndexingProvider { get; private set; }
        public IArchivalProvider<T> ArchivalProvider { get; private set; }
        public IPurgeProvider<T> PurgeProvider { get; private set; }
        public IBackupProvider<T> BackupProvider { get; private set; }
        public IMigrationProvider<T> MigrationProvider { get; private set; }
        protected ISerializer<T> Serializer { get; private set; }
        protected IConfigReader ConfigReader { get;}

        protected BaseProvider(IServiceProvider serviceProvider, string name)
        {
            this.Name = name;
            this.ServiceProvider = serviceProvider;
            this.ConfigReader = serviceProvider.GetRequiredService<IConfigReader>();
            this.ReadCapabilities(this.ConfigReader);
        }

        protected BaseProvider(UnityContainer container, string name)
        {
            this.Container = container;
            this.Name = name;
            this.ConfigReader = container.Resolve<IConfigReader>();
            this.ReadCapabilities(this.ConfigReader);
        }

        protected TProvider Get<TProvider>() where TProvider : class
        {
            if (this.ServiceProvider != null)
            {
                return this.ServiceProvider.GetRequiredService<TProvider>();
            }

            if (this.Container != null)
            {
                return this.Container.Resolve<TProvider>();
            }

            throw new InvalidOperationException($"provider {typeof(TProvider).Name} for {typeof(T).Name} is not registered");
        }

        protected ILogger<T> GetLogger<T>()
        {
            var loggerFactory = this.Get<ILoggerFactory>();
            return loggerFactory.CreateLogger<T>();
        }

        private void ReadCapabilities(IConfigReader configReader)
        {
            this.Capabilities = configReader.GetProviderCapabilities(this.Name);
            var serializerFactory = this.Get<ISerializerFactory>();
            this.Serializer = serializerFactory.Create<T>(this.Name);
        }
    }
}