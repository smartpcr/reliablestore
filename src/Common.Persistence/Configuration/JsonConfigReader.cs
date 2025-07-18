﻿// -----------------------------------------------------------------------
// <copyright file="JsonConfigReader.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Configuration
{
    using System;
    using System.Collections.Generic;
    using Common.Persistence.Contract;
    using Common.Persistence.Factory;
    using Microsoft.Extensions.Configuration;

    public class JsonConfigReader : IConfigReader
    {
        private readonly DIContainerWrapper wrapper;
        private readonly IConfiguration configuration;

        public JsonConfigReader(DIContainerWrapper wrapper)
        {
            this.wrapper = wrapper;
            this.configuration = wrapper.Get<IConfiguration>();
        }

        public T ReadSettings<T>(string name) where T : class, new()
        {
            return this.configuration.GetConfiguredSettings<T>($"Providers:{name}");
        }

        public ProviderCapability GetProviderCapabilities(string name)
        {
            var section = this.configuration.GetSection($"Providers:{name}");
            var capabilitiesString = section["Capabilities"];

            if (Enum.TryParse<ProviderCapability>(capabilitiesString, out var capabilities))
            {
                return capabilities;
            }

            return ProviderCapability.None;
        }

        public CrudStorageProviderSettings GetCrudStorageProviderSettings(string name)
        {
            var settings = this.wrapper.Get<CrudStorageProviderSettings>(name);
            if (settings == null)
            {
                throw new KeyNotFoundException($"CrudStorageProvider settings for {name} not found.");
            }
            return settings;
        }

        public IndexingProviderSettings GetIndexProviderSettings(string name)
        {
            var settings = this.wrapper.Get<IndexingProviderSettings>(name);
            if (settings == null)
            {
                throw new KeyNotFoundException($"IndexingProvider settings for {name} not found.");
            }
            return settings;
        }

        public BackupProviderSettings GetBackupProviderSettings(string name)
        {
            throw new System.NotImplementedException();
        }

        public MigrationProviderSettings GetMigrationProviderSettings(string name)
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