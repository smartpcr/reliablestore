// -----------------------------------------------------------------------
// <copyright file="InMemoryStoreSettings.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Providers.InMemory
{
    using System;
    using Common.Persistence.Configuration;

    public class InMemoryStoreSettings : CrudStorageProviderSettings
    {
        public override string Name { get; set; } = "InMemory";
        public override string AssemblyName { get; set; } = typeof(InMemoryProvider<>).Assembly.FullName!;
        public override string TypeName { get; set; } = typeof(InMemoryProvider<>).FullName!;
        public override bool Enabled { get; set; } = true;
        public TimeSpan DefaultTTL { get; set; } = TimeSpan.FromHours(1);
        public int MaxCacheSize { get; set; } = 10000;
        public bool EnableEviction { get; set; } = true;
        public TimeSpan EvictionInterval { get; set; } = TimeSpan.FromDays(30);
        public string EvictionStrategy { get; set; } = "LRU";
    }
}