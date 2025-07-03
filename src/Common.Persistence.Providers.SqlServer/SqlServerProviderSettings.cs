//-------------------------------------------------------------------------------
// <copyright file="SqlServerProviderSettings.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------


namespace Common.Persistence.Providers.SqlServer
{
    using Common.Persistence.Configuration;
    using Common.Persistence.Contract;

    public class SqlServerProviderSettings : CrudStorageProviderSettings
    {
        public override string Name { get; set; } = "SqlServer";
        public override string AssemblyName { get; set; } = typeof(SqlServerProviderSettings).Assembly.FullName!;
        public override string TypeName { get; set; } = typeof(SqlServerProvider<>).FullName!;
        public override bool Enabled { get; set; } = true;

        public ProviderCapability Capabilities { get; set; } = ProviderCapability.Crud;

        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 1433;
        public string DbName { get; set; } = "ReliableStore";
        public string UserId { get; set; } = "sa";
        public string Password { get; set; }
        public int CommandTimeout { get; set; } = 30;

        public bool EnableRetryLogic { get; set; } = true;

        public int MaxRetryCount { get; set; } = 3;

        public bool CreateTableIfNotExists { get; set; } = true;

        public bool CreateDatabaseIfNotExists { get; set; } = true;

        public string GetConnectionString()
        {
            return $"Server={this.Host},{this.Port};Database={this.DbName};User Id={this.UserId};Password={this.Password};TrustServerCertificate=true";
        }
    }
}