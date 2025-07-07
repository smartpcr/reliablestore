//-------------------------------------------------------------------------------
// <copyright file="SqlServerProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.SqlServer
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Persistence.Contract;
    using Microsoft.Data.SqlClient;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Unity;

    public partial class SqlServerProvider<T> : ICrudStorageProvider<T> where T : IEntity
    {
        private readonly SqlServerProviderSettings settings;
        private readonly ILogger<SqlServerProvider<T>> logger;
        private readonly string connectionString;
        private readonly string tableName;
        private readonly string schemaName;
        private readonly string fullTableName;
        private readonly SemaphoreSlim initLock = new(1, 1);
        private bool initialized;

        public SqlServerProvider(IServiceProvider serviceProvider, string name)
            : base(serviceProvider, name)
        {
            this.settings = this.ConfigReader.ReadSettings<SqlServerProviderSettings>(name)
                            ?? throw new InvalidOperationException($"Settings not found for provider '{name}'");
            this.logger = this.GetLogger<SqlServerProvider<T>>();
            this.connectionString = this.settings.GetConnectionString();
            this.tableName = $"{typeof(T).Name}";
            this.schemaName = string.IsNullOrWhiteSpace(this.settings.Schema) ? "dbo" : this.settings.Schema;
            this.fullTableName = $"[{this.schemaName}].[{this.tableName}]";
        }

        public SqlServerProvider(UnityContainer container, string name)
            : base(container, name)
        {
            this.settings = this.ConfigReader.ReadSettings<SqlServerProviderSettings>(name)
                            ?? throw new InvalidOperationException($"Settings not found for provider '{name}'");
            this.logger = this.GetLogger<SqlServerProvider<T>>();
            this.connectionString = this.settings.GetConnectionString();
            this.tableName = $"{typeof(T).Name}";
            this.schemaName = string.IsNullOrWhiteSpace(this.settings.Schema) ? "dbo" : this.settings.Schema;
            this.fullTableName = $"[{this.schemaName}].[{this.tableName}]";
        }

        private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
        {
            if (this.initialized) return;

            await this.initLock.WaitAsync(cancellationToken);
            try
            {
                if (this.initialized) return;

                // Test connection first with a shorter timeout
                await this.TestConnectionAsync(cancellationToken);

                await this.CreateSchemaIfNotExistsAsync(cancellationToken);

                if (this.settings.CreateTableIfNotExists)
                {
                    await this.CreateTableIfNotExistsAsync(cancellationToken);
                }

                this.initialized = true;
            }
            finally
            {
                this.initLock.Release();
            }
        }

        private async Task TestConnectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Create a connection string with a shorter timeout for testing
                var builder = new SqlConnectionStringBuilder(this.connectionString)
                {
                    ConnectTimeout = 5 // Quick test timeout
                };
                
                await using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync(cancellationToken);
                this.logger.LogInformation("Successfully connected to SQL Server at {Host}:{Port}", this.settings.Host, this.settings.Port);
            }
            catch (SqlException ex)
            {
                this.logger.LogError(ex, "Failed to connect to SQL Server at {Host}:{Port}", this.settings.Host, this.settings.Port);
                var authMode = this.settings.IntegratedSecurity ? "Windows Authentication" : "SQL Server Authentication";
                throw new InvalidOperationException(
                    $"Cannot connect to SQL Server at {this.settings.Host}:{this.settings.Port} using {authMode}. " +
                    "Please ensure:\n" +
                    "1. SQL Server is running and accessible\n" +
                    "2. Authentication credentials are correct\n" +
                    "3. Firewall allows connections on port 1433\n\n" +
                    "For local development with Docker:\n" +
                    "docker run -e 'ACCEPT_EULA=Y' -e 'SA_PASSWORD=YourStrong@Passw0rd' -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest",
                    ex);
            }
            catch (InvalidOperationException ex)
            {
                this.logger.LogError(ex, "Invalid SQL Server configuration");
                throw;
            }
        }


        private async Task CreateSchemaIfNotExistsAsync(CancellationToken cancellationToken)
        {
            if (this.schemaName.Equals("dbo", StringComparison.OrdinalIgnoreCase))
            {
                return; // dbo schema always exists
            }

            const string createSchemaSql = @"
                IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = @SchemaName)
                BEGIN
                    EXEC('CREATE SCHEMA [' + @SchemaName + ']')
                END";

            await using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(createSchemaSql, connection);
            command.Parameters.AddWithValue("@SchemaName", this.schemaName);
            command.CommandTimeout = this.settings.CommandTimeout;

            await command.ExecuteNonQueryAsync(cancellationToken);
            this.logger.LogInformation("Ensured schema {SchemaName} exists", this.schemaName);
        }

        private async Task CreateTableIfNotExistsAsync(CancellationToken cancellationToken)
        {
            const string createTableSql = @"
                IF NOT EXISTS (SELECT * FROM sys.tables t 
                               JOIN sys.schemas s ON t.schema_id = s.schema_id 
                               WHERE t.name = @TableName AND s.name = @SchemaName)
                BEGIN
                    CREATE TABLE {0} (
                        [Key] NVARCHAR(450) NOT NULL PRIMARY KEY,
                        [Data] NVARCHAR(MAX) NOT NULL,
                        [Version] BIGINT NOT NULL,
                        [ETag] NVARCHAR(450) NULL,
                        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                        [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
                    )

                    CREATE INDEX IX_{1}_{2}_Version ON {0} ([Version])
                    CREATE INDEX IX_{1}_{2}_UpdatedAt ON {0} ([UpdatedAt])
                END";

            await using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(string.Format(createTableSql, this.fullTableName, this.schemaName, this.tableName), connection);
            command.Parameters.AddWithValue("@TableName", this.tableName);
            command.Parameters.AddWithValue("@SchemaName", this.schemaName);
            command.CommandTimeout = this.settings.CommandTimeout;

            await command.ExecuteNonQueryAsync(cancellationToken);
            this.logger.LogInformation("Ensured table {FullTableName} exists", this.fullTableName);
        }

        public async Task<T?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            await this.EnsureInitializedAsync(cancellationToken);

            await using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand($"SELECT [Data] FROM {this.fullTableName} WHERE [Key] = @Key", connection);
            command.Parameters.AddWithValue("@Key", key);
            command.CommandTimeout = this.settings.CommandTimeout;

            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result == null || result == DBNull.Value)
            {
                return default;
            }

            var json = (string)result;
            return JsonConvert.DeserializeObject<T>(json);
        }

        public async Task<IEnumerable<T>> GetManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            await this.EnsureInitializedAsync(cancellationToken);

            var keyList = keys.ToList();
            if (!keyList.Any())
            {
                return Enumerable.Empty<T>();
            }

            await using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);

            var paramNames = keyList.Select((_, i) => $"@key{i}").ToList();
            var sql = $"SELECT [Data] FROM {this.fullTableName} WHERE [Key] IN ({string.Join(",", paramNames)})";

            await using var command = new SqlCommand(sql, connection);
            for (var i = 0; i < keyList.Count; i++)
            {
                command.Parameters.AddWithValue($"@key{i}", keyList[i]);
            }
            command.CommandTimeout = this.settings.CommandTimeout;

            var results = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var json = reader.GetString(0);
                var entity = JsonConvert.DeserializeObject<T>(json);
                if (entity != null)
                {
                    results.Add(entity);
                }
            }

            return results;
        }

        public async Task<IEnumerable<T>> GetAllAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            await this.EnsureInitializedAsync(cancellationToken);

            await using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = $"SELECT [Data] FROM {this.fullTableName}";
            await using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = this.settings.CommandTimeout;

            var results = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var json = reader.GetString(0);
                var entity = JsonConvert.DeserializeObject<T>(json);
                if (entity != null)
                {
                    results.Add(entity);
                }
            }

            if (predicate != null)
            {
                var compiledPredicate = predicate.Compile();
                results = results.Where(compiledPredicate).ToList();
            }

            return results;
        }

        public async Task SaveAsync(string key, T entity, CancellationToken cancellationToken = default)
        {
            await this.EnsureInitializedAsync(cancellationToken);

            var json = JsonConvert.SerializeObject(entity);

            await using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = $@"
                MERGE {this.fullTableName} AS target
                USING (SELECT @Key AS [Key]) AS source
                ON target.[Key] = source.[Key]
                WHEN MATCHED THEN
                    UPDATE SET
                        [Data] = @Data,
                        [Version] = @Version,
                        [ETag] = @ETag,
                        [UpdatedAt] = GETUTCDATE()
                WHEN NOT MATCHED THEN
                    INSERT ([Key], [Data], [Version], [ETag])
                    VALUES (@Key, @Data, @Version, @ETag);";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Key", key);
            command.Parameters.AddWithValue("@Data", json);
            command.Parameters.AddWithValue("@Version", entity.Version);
            command.Parameters.AddWithValue("@ETag", (object?)entity.ETag ?? DBNull.Value);
            command.CommandTimeout = this.settings.CommandTimeout;

            await this.ExecuteWithRetryAsync(async () => await command.ExecuteNonQueryAsync(cancellationToken), cancellationToken);

            this.logger.LogDebug("Saved entity with key {Key} to table {TableName}", key, this.tableName);
        }

        public async Task SaveManyAsync(IEnumerable<KeyValuePair<string, T>> entities, CancellationToken cancellationToken = default)
        {
            await this.EnsureInitializedAsync(cancellationToken);

            var entityList = entities.ToList();
            if (!entityList.Any())
            {
                return;
            }

            await using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var transaction = connection.BeginTransaction();
            try
            {
                foreach (var kvp in entityList)
                {
                    var json = JsonConvert.SerializeObject(kvp.Value);

                    var sql = $@"
                        MERGE {this.fullTableName} AS target
                        USING (SELECT @Key AS [Key]) AS source
                        ON target.[Key] = source.[Key]
                        WHEN MATCHED THEN
                            UPDATE SET
                                [Data] = @Data,
                                [Version] = @Version,
                                [ETag] = @ETag,
                                [UpdatedAt] = GETUTCDATE()
                        WHEN NOT MATCHED THEN
                            INSERT ([Key], [Data], [Version], [ETag])
                            VALUES (@Key, @Data, @Version, @ETag);";

                    await using var command = new SqlCommand(sql, connection, transaction);
                    command.Parameters.AddWithValue("@Key", kvp.Key);
                    command.Parameters.AddWithValue("@Data", json);
                    command.Parameters.AddWithValue("@Version", kvp.Value.Version);
                    command.Parameters.AddWithValue("@ETag", (object?)kvp.Value.ETag ?? DBNull.Value);
                    command.CommandTimeout = this.settings.CommandTimeout;

                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                this.logger.LogDebug("Saved {Count} entities to table {TableName}", entityList.Count, this.tableName);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            await this.EnsureInitializedAsync(cancellationToken);

            await using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand($"DELETE FROM {this.fullTableName} WHERE [Key] = @Key", connection);
            command.Parameters.AddWithValue("@Key", key);
            command.CommandTimeout = this.settings.CommandTimeout;

            var rowsAffected = await this.ExecuteWithRetryAsync(async () => await command.ExecuteNonQueryAsync(cancellationToken), cancellationToken);

            if (rowsAffected > 0)
            {
                this.logger.LogDebug("Deleted entity with key {Key} from table {TableName}", key, this.tableName);
            }
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            await this.EnsureInitializedAsync(cancellationToken);

            await using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand($"SELECT COUNT(1) FROM {this.fullTableName} WHERE [Key] = @Key", connection);
            command.Parameters.AddWithValue("@Key", key);
            command.CommandTimeout = this.settings.CommandTimeout;

            var count = (int)await command.ExecuteScalarAsync(cancellationToken);
            return count > 0;
        }

        public async Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            await this.EnsureInitializedAsync(cancellationToken);

            if (predicate == null)
            {
                await using var connection = new SqlConnection(this.connectionString);
                await connection.OpenAsync(cancellationToken);

                await using var command = new SqlCommand($"SELECT COUNT(1) FROM {this.fullTableName}", connection);
                command.CommandTimeout = this.settings.CommandTimeout;

                return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            }
            else
            {
                var allEntities = await this.GetAllAsync(null, cancellationToken);
                var compiledPredicate = predicate.Compile();
                return allEntities.Where(compiledPredicate).LongCount();
            }
        }

        public async Task<long> ClearAsync(CancellationToken cancellationToken = default)
        {
            await this.EnsureInitializedAsync(cancellationToken);

            await using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand($"DELETE FROM {this.fullTableName}; SELECT @@ROWCOUNT", connection);
            command.CommandTimeout = this.settings.CommandTimeout;

            var rowsDeleted = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));

            this.logger.LogInformation("Cleared {RowsDeleted} rows from table {TableName}", rowsDeleted, this.tableName);
            return rowsDeleted;
        }

        private async Task<T1> ExecuteWithRetryAsync<T1>(Func<Task<T1>> operation, CancellationToken cancellationToken)
        {
            if (!this.settings.EnableRetryLogic)
            {
                return await operation();
            }

            var retryCount = 0;
            while (true)
            {
                try
                {
                    return await operation();
                }
                catch (SqlException ex) when (IsTransientError(ex) && retryCount < this.settings.MaxRetryCount)
                {
                    retryCount++;
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
                    this.logger.LogWarning(ex, "Transient SQL error occurred. Retrying in {Delay} seconds. Retry {RetryCount}/{MaxRetryCount}",
                        delay.TotalSeconds, retryCount, this.settings.MaxRetryCount);

                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        private static bool IsTransientError(SqlException ex)
        {
            var transientErrors = new[] { 1205, 1222, 49918, 49919, 49920, 4060, 40197, 40501, 40613, 64 };
            return transientErrors.Contains(ex.Number);
        }

        public void Dispose()
        {
            this.initLock.Dispose();
        }
    }
}