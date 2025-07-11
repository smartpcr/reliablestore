﻿// -----------------------------------------------------------------------
// <copyright file="IConfigReader.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Configuration
{
    using System.Collections.Generic;
    using Common.Persistence.Contract;

    public interface IConfigReader
    {
        T ReadSettings<T>(string name) where T : class, new();

        ProviderCapability GetProviderCapabilities(string name);

        CrudStorageProviderSettings GetCrudStorageProviderSettings(string name);

        IndexingProviderSettings GetIndexProviderSettings(string name);

        BackupProviderSettings GetBackupProviderSettings(string name);

        MigrationProviderSettings GetMigrationProviderSettings(string name);

        ArchivalProviderSettings GetArchivalProviderSettings(string name);

        PurgeProviderSettings GetPurgeProviderSettings(string name);

    }
}