// -----------------------------------------------------------------------
// <copyright file="JsonConfigReader.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Configuration
{
    using System.Collections.Generic;
    using Common.Persistence.Contract;

    public class JsonConfigReader : IConfigReader
    {
        public T ReadSettings<T>() where T : class
        {
            throw new System.NotImplementedException();
        }

        public IReadOnlyList<PersistenceProviderSettings> ReadPersistenceProviderSettings()
        {
            throw new System.NotImplementedException();
        }

        public ProviderCapability GetProviderCapabilities(string name)
        {
            throw new System.NotImplementedException();
        }

        public CrudStorageProviderSettings ReadCrudStorageProviderSettings(string name)
        {
            throw new System.NotImplementedException();
        }

        public IndexingProviderSettings GetIndexProviderSettings(string name)
        {
            throw new System.NotImplementedException();
        }

        public BackupProviderSettings GetBackupProviderSettings(string name)
        {
            throw new System.NotImplementedException();
        }

        public MigrationProviderSettings GetMigrationProviderSettings(string name)
        {
            throw new System.NotImplementedException();
        }

        public SerializerProviderSettings GetSerializerProviderSettings(string name)
        {
            throw new System.NotImplementedException();
        }

        public ArchivalProviderSettings GetArchivalProviderSettings(string name)
        {
            throw new System.NotImplementedException();
        }

        public PurgeProviderSettings GetPurgeProviderSettings(string name)
        {
            throw new System.NotImplementedException();
        }
    }
}