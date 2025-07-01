//-------------------------------------------------------------------------------
// <copyright file="EsentStoreSettings.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.Esent
{
    using Common.Persistence.Configuration;

    public class EsentStoreSettings : CrudStorageProviderSettings
    {
        public override string Name { get; set; } = "Esent";
        public override string AssemblyName { get; set; } = typeof(EsentStoreSettings).Assembly.FullName!;
        public override string TypeName { get; set; } = typeof(EsentProvider<>).FullName!;
        public override bool Enabled { get; set; } = true;
        /// <summary>
        /// Gets or sets the database file path.
        /// </summary>
        public string DatabasePath { get; set; } = "data/esent.db";

        /// <summary>
        /// Gets or sets the instance name for ESENT.
        /// </summary>
        public string InstanceName { get; set; } = "ReliableStore";

        /// <summary>
        /// Gets or sets the maximum database size in MB.
        /// </summary>
        public int MaxDatabaseSizeMB { get; set; } = 1024;

        /// <summary>
        /// Gets or sets the cache size in MB.
        /// </summary>
        public int CacheSizeMB { get; set; } = 64;

        /// <summary>
        /// Gets or sets whether to enable versioning.
        /// </summary>
        public bool EnableVersioning { get; set; } = true;

        /// <summary>
        /// Gets or sets the page size in KB (must be 2, 4, 8, 16, or 32).
        /// </summary>
        public int PageSizeKB { get; set; } = 8;

        /// <summary>
        /// Gets or sets whether to use session pooling for improved performance.
        /// Default is false for backward compatibility.
        /// </summary>
        public bool UseSessionPool { get; set; } = false;
    }
}