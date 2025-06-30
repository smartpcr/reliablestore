// -----------------------------------------------------------------------
// <copyright file="Factories.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Factory
{
    using System;
    using System.Reflection;
    using Common.Persistence.Configuration;
    using Common.Persistence.Contract;
    using Common.Persistence.Serialization;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Unity;

    // --------------
    // (A) CRUD factory
    // --------------
    public interface ICrudStorageProviderFactory
    {
        ICrudStorageProvider<T> Create<T>(string name) where T : IEntity;
    }

    public class CrudStorageProviderFactory : ICrudStorageProviderFactory
    {
        private readonly DIContainerWrapper containerWrapper;
        private readonly IConfigReader configReader;

        public CrudStorageProviderFactory(DIContainerWrapper containerWrapper, IConfigReader configReader)
        {
            this.containerWrapper = containerWrapper;
            this.configReader = configReader;
        }

        public ICrudStorageProvider<T> Create<T>(string name) where T : IEntity
        {
            var crudProviderSettings = this.configReader.ReadCrudStorageProviderSettings(name);
            var ctor = crudProviderSettings.FindConstructor<T>();
            return this.containerWrapper.TryRegisterAndGetRequired<ICrudStorageProvider<T>>(name, ctor);
        }
    }


    // --------------
    // (B) Indexing factory
    // --------------
    public interface IIndexingProviderFactory
    {
        IIndexingProvider<T> Create<T>(string name) where T : IEntity;
    }

    public class IndexingProviderFactory : IIndexingProviderFactory
    {
        private readonly DIContainerWrapper containerWrapper;
        private readonly IConfigReader configReader;

        public IndexingProviderFactory(DIContainerWrapper containerWrapper, IConfigReader configReader)
        {
            this.containerWrapper = containerWrapper;
            this.configReader = configReader;
        }

        public IIndexingProvider<T> Create<T>(string name) where T : IEntity
        {
            var indexProviderSettings = this.configReader.GetIndexProviderSettings(name);
            var ctor = indexProviderSettings.FindConstructor<T>();
            return this.containerWrapper.TryRegisterAndGetRequired<IIndexingProvider<T>>(name, ctor);
        }
    }

    // --------------
    // (C) Archival factory
    // --------------
    public interface IArchivalProviderFactory
    {
        IArchivalProvider<T> Create<T>(string name) where T : IEntity;
    }

    public class ArchivalProviderFactory : IArchivalProviderFactory
    {
        private readonly DIContainerWrapper containerWrapper;
        private readonly IConfigReader configReader;

        public ArchivalProviderFactory(DIContainerWrapper containerWrapper, IConfigReader configReader)
        {
            this.containerWrapper = containerWrapper;
            this.configReader = configReader;
        }

        public IArchivalProvider<T> Create<T>(string name) where T : IEntity
        {
            var archivalProviderSettings = this.configReader.GetArchivalProviderSettings(name);
            var ctor = archivalProviderSettings.FindConstructor<T>();
            return this.containerWrapper.TryRegisterAndGetRequired<IArchivalProvider<T>>(name, ctor);
        }
    }

    // --------------
    // (D) Purge factory
    // --------------
    public interface IPurgeProviderFactory
    {
        IPurgeProvider<T> Create<T>(string name) where T : IEntity;
    }

    public class PurgeProviderFactory : IPurgeProviderFactory
    {
        private readonly DIContainerWrapper containerWrapper;
        private readonly IConfigReader configReader;

        public PurgeProviderFactory(DIContainerWrapper containerWrapper, IConfigReader configReader)
        {
            this.containerWrapper = containerWrapper;
            this.configReader = configReader;
        }

        public IPurgeProvider<T> Create<T>(string name) where T : IEntity
        {
            var purgeProviderSettings = this.configReader.GetPurgeProviderSettings(name);
            var ctor = purgeProviderSettings.FindConstructor<T>();
            return this.containerWrapper.TryRegisterAndGetRequired<IPurgeProvider<T>>(name, ctor);
        }
    }

    // --------------
    // (E) Backup factory
    // --------------
    public interface IBackupProviderFactory
    {
        IBackupProvider<T> Create<T>(string name) where T : IEntity;
    }

    public class BackupProviderFactory : IBackupProviderFactory
    {
        private readonly DIContainerWrapper containerWrapper;
        private readonly IConfigReader configReader;

        public BackupProviderFactory(DIContainerWrapper containerWrapper, IConfigReader configReader)
        {
            this.containerWrapper = containerWrapper;
            this.configReader = configReader;
        }

        public IBackupProvider<T> Create<T>(string name) where T : IEntity
        {
            var backupProviderSettings = this.configReader.GetBackupProviderSettings(name);
            var ctor = backupProviderSettings.FindConstructor<T>();
            return this.containerWrapper.TryRegisterAndGetRequired<IBackupProvider<T>>(name, ctor);
        }
    }

    // --------------
    // (F) Migration factory
    // --------------
    public interface IMigrationProviderFactory
    {
        IMigrationProvider<T> Create<T>(string name) where T : IEntity;
    }

    public class MigrationProviderFactory : IMigrationProviderFactory
    {
        private readonly DIContainerWrapper containerWrapper;
        private readonly IConfigReader configReader;

        public MigrationProviderFactory(DIContainerWrapper containerWrapper, IConfigReader configReader)
        {
            this.containerWrapper = containerWrapper;
            this.configReader = configReader;
        }

        public IMigrationProvider<T> Create<T>(string name) where T : IEntity
        {
            var migrationProviderSettings = this.configReader.GetMigrationProviderSettings(name);
            var ctor = migrationProviderSettings.FindConstructor<T>();
            return this.containerWrapper.TryRegisterAndGetRequired<IMigrationProvider<T>>(name, ctor);
        }
    }

    // --------------
    // (G) Serializer factory
    // --------------
    public interface ISerializerFactory
    {
        ISerializer<T> Create<T>(string name) where T : IEntity;
    }

    public class SerializerFactory : ISerializerFactory
    {
        private readonly DIContainerWrapper containerWrapper;
        private readonly IConfigReader configReader;

        public SerializerFactory(DIContainerWrapper containerWrapper, IConfigReader configReader)
        {
            this.containerWrapper = containerWrapper;
            this.configReader = configReader;
        }

        public ISerializer<T> Create<T>(string name) where T : IEntity
        {
            return new JsonSerializer<T>();
        }
    }
}


