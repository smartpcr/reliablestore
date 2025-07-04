//-------------------------------------------------------------------------------
// <copyright file="SQLiteProviderSettings.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.SQLite
{
    using System;
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
        /// Gets the connection string.
        /// </summary>
        public string GetConnectionString()
        {
            var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = this.DataSource,
                Mode = Enum.Parse<Microsoft.Data.Sqlite.SqliteOpenMode>(this.Mode),
                Cache = Enum.Parse<Microsoft.Data.Sqlite.SqliteCacheMode>(this.Cache),
                ForeignKeys = this.ForeignKeys,
                DefaultTimeout = this.CommandTimeout
            };

            return builder.ConnectionString;
        }
    }
}