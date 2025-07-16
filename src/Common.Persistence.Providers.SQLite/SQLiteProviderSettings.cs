//-------------------------------------------------------------------------------
// <copyright file="SQLiteProviderSettings.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.SQLite
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using Common.Persistence.Configuration;
    using Common.Persistence.Contract;

    public class SQLiteProviderSettings : CrudStorageProviderSettings
    {
        public override string Name { get; set; } = "SQLite";
        public override string AssemblyName { get; set; } = typeof(SQLiteProviderSettings).Assembly.FullName!;
        public override string TypeName { get; set; } = typeof(SQLiteProvider<>).FullName!;
        public override bool Enabled { get; set; } = true;

        public ProviderCapability Capabilities { get; set; } = ProviderCapability.Crud;

        /// <summary>
        /// Gets or sets the data source (file path or connection string).
        /// Use ":memory:" for in-memory database.
        /// </summary>
        public string DataSource { get; set; } = "reliablestore.db";

        /// <summary>
        /// Gets or sets the connection mode.
        /// </summary>
        public string Mode { get; set; } = "ReadWriteCreate";

        /// <summary>
        /// Gets or sets the cache mode.
        /// </summary>
        public string Cache { get; set; } = "Shared";

        /// <summary>
        /// Gets or sets whether foreign keys are enabled.
        /// </summary>
        public bool ForeignKeys { get; set; } = true;

        /// <summary>
        /// Gets or sets the command timeout in seconds.
        /// </summary>
        public int CommandTimeout { get; set; } = 30;

        /// <summary>
        /// Gets or sets whether to create table if not exists.
        /// </summary>
        public bool CreateTableIfNotExists { get; set; } = true;

        /// <summary>
        /// Gets or sets the schema name (SQLite doesn't support schemas, but we'll use it for table prefixing).
        /// </summary>
        public string Schema { get; set; } = string.Empty;

        /// <summary>
        /// The suggested maximum number of database pages SQLite will hold in RAM per open database connection.
        /// It's an upper bound on the page cache. Default value is '-2000', which means "enough pages to use ≈ 2000 × 1024 bytes"
        /// of memory (~2MB), regardless of the page size.
        /// N &gt; 0: sets the cache to N pages.
        /// K &lt; 0: sets the cache so that K × 1024 bytes of memory is used
        /// </summary>
        public int CacheSize { get; set; } = -2000; // 2MB cache

        /// <summary>
        /// The unit of I/O in SQLite, default to 4096 bytes per page.
        /// Every database file is a sequence of fixed-size pages; internal B-tree nodes, table rows, index entries—all live inside pages.
        /// </summary>
        [Range(512, 65536)]
        public int PageSize { get; set; } = 4096;

        /// <summary>
        /// How logs are maintained.
        /// </summary>
        public JournalMode JournalMode { get; set; } = JournalMode.WAL;

        /// <summary>
        /// Manages how often fsync() should be called.
        /// </summary>
        public SynchronousMode SynchronousMode { get; set; } = SynchronousMode.Normal;

        /// <summary>
        /// Gets the connection string.
        /// </summary>
        public string GetConnectionString()
        {
            var dataSource = this.DataSource;
            
            // Convert relative paths to absolute paths (except for :memory:)
            if (!dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase) && 
                !System.IO.Path.IsPathRooted(dataSource))
            {
                dataSource = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), dataSource);
            }
            
            var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = dataSource,
                Mode = Enum.Parse<Microsoft.Data.Sqlite.SqliteOpenMode>(this.Mode),
                Cache = Enum.Parse<Microsoft.Data.Sqlite.SqliteCacheMode>(this.Cache),
                ForeignKeys = this.ForeignKeys,
                DefaultTimeout = this.CommandTimeout
            };

            return builder.ConnectionString;
        }
    }
}