//-------------------------------------------------------------------------------
// <copyright file="SqlServerProviderSettings.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------


namespace Common.Persistence.Providers.SqlServer
{
    using System;
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
        public string? UserId { get; set; } = "sa";
        public string? Password { get; set; }
        public bool IntegratedSecurity { get; set; } = false;
        public int CommandTimeout { get; set; } = 30;

        public bool EnableRetryLogic { get; set; } = true;

        public int MaxRetryCount { get; set; } = 3;

        public bool CreateTableIfNotExists { get; set; } = true;

        public string Schema { get; set; } = "dbo";

        public int ConnectTimeout { get; set; } = 30;

        public string GetConnectionString()
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
            {
                DataSource = $"{this.Host},{this.Port}",
                InitialCatalog = this.DbName,
                TrustServerCertificate = true,
                ConnectTimeout = this.ConnectTimeout,
                CommandTimeout = this.CommandTimeout
            };

            // Use Windows Authentication or SQL Server Authentication
            if (this.IntegratedSecurity)
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                // Validate required parameters for SQL Server Authentication
                if (string.IsNullOrEmpty(this.UserId))
                {
                    throw new InvalidOperationException("UserId is required when IntegratedSecurity is false");
                }
                if (string.IsNullOrEmpty(this.Password))
                {
                    throw new InvalidOperationException("Password is required when IntegratedSecurity is false");
                }
                
                builder.UserID = this.UserId;
                builder.Password = this.Password;
            }

            if (this.EnableRetryLogic)
            {
                builder.ConnectRetryCount = this.MaxRetryCount;
                builder.ConnectRetryInterval = 10;
            }

            return builder.ConnectionString;
        }
    }
}