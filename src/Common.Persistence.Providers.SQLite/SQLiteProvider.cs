//-------------------------------------------------------------------------------
// <copyright file="SQLiteProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.SQLite
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Persistence.Contract;
    using Microsoft.Data.Sqlite;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Unity;

    public partial class SQLiteProvider<T> : ICrudStorageProvider<T> where T : IEntity
    {
        private readonly SQLiteProviderSettings settings;
        private readonly ILogger<SQLiteProvider<T>> logger;
        private readonly string connectionString;
        private readonly string tableName;
        private readonly SemaphoreSlim initLock = new(1, 1);
        private bool initialized;

        public SQLiteProvider(IServiceProvider serviceProvider, string name)
            : base(serviceProvider, name)
        {
            this.settings = this.ConfigReader.ReadSettings<SQLiteProviderSettings>(name)
                            ?? throw new InvalidOperationException($"Settings not found for provider '{name}'");
            this.logger = this.GetLogger<SQLiteProvider<T>>();
            this.connectionString = this.settings.GetConnectionString();

            // SQLite doesn't support schemas, so we use schema as a table prefix
            var prefix = string.IsNullOrWhiteSpace(this.settings.Schema) ? string.Empty : $"{this.settings.Schema}_";
            this.tableName = $"{prefix}{typeof(T).Name}";
        }

        public SQLiteProvider(UnityContainer container, string name)
            : base(container, name)
        {
            this.settings = this.ConfigReader.ReadSettings<SQLiteProviderSettings>(name)
                            ?? throw new InvalidOperationException($"Settings not found for provider '{name}'");
            this.logger = this.GetLogger<SQLiteProvider<T>>();
            this.connectionString = this.settings.GetConnectionString();

            // SQLite doesn't support schemas, so we use schema as a table prefix
            var prefix = string.IsNullOrWhiteSpace(this.settings.Schema) ? string.Empty : $"{this.settings.Schema}_";
            this.tableName = $"{prefix}{typeof(T).Name}";
        }

        private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
        {
            if (this.initialized) return;

            await this.initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (this.initialized) return;

                // Ensure the directory exists if using a file-based database
                if (!this.settings.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
                {
                    var directory = Path.GetDirectoryName(this.settings.DataSource);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                        this.logger.LogInformation("Created directory for SQLite database: {Directory}", directory);
                    }
                }

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

        private async Task CreateTableIfNotExistsAsync(CancellationToken cancellationToken)
        {
            var createTableSql = $@"
                CREATE TABLE IF NOT EXISTS [{this.tableName}] (
                    [Key] TEXT NOT NULL PRIMARY KEY,
                    [Data] TEXT NOT NULL,
                    [Version] INTEGER NOT NULL,
                    [ETag] TEXT NULL,
                    [CreatedAt] TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    [UpdatedAt] TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );

                CREATE INDEX IF NOT EXISTS IX_{this.tableName}_Version ON [{this.tableName}] ([Version]);
                CREATE INDEX IF NOT EXISTS IX_{this.tableName}_UpdatedAt ON [{this.tableName}] ([UpdatedAt]);";

            await using var connection = new SqliteConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqliteCommand(createTableSql, connection);
            command.CommandTimeout = this.settings.CommandTimeout;

            await command.ExecuteNonQueryAsync(cancellationToken);
            this.logger.LogInformation("Ensured table {TableName} exists", this.tableName);
        }

        public async Task<T?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            await this.EnsureInitializedAsync(cancellationToken);

            await using var connection = new SqliteConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqliteCommand($"SELECT [Data] FROM [{this.tableName}] WHERE [Key] = @Key", connection);
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
            if (keyList.Count == 0)
            {
                return Enumerable.Empty<T>();
            }

            await using var connection = new SqliteConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);

            var paramNames = keyList.Select((_, i) => $"@key{i}").ToList();
            var sql = $"SELECT [Data] FROM [{this.tableName}] WHERE [Key] IN ({string.Join(",", paramNames)})";

            await using var command = new SqliteCommand(sql, connection);
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

            await using var connection = new SqliteConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = $"SELECT [Data] FROM [{this.tableName}]";
            await using var command = new SqliteCommand(sql, connection);
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
                return results.Where(compiledPredicate);
            }

            return results;
        }

        public async Task SaveAsync(string key, T entity, CancellationToken cancellationToken = default)
        {
            await this.EnsureInitializedAsync(cancellationToken);

            var json = JsonConvert.SerializeObject(entity);
            var etag = Guid.NewGuid().ToString();

            await using var connection = new SqliteConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = $@"
                INSERT INTO [{this.tableName}] ([Key], [Data], [Version], [ETag], [UpdatedAt])
                VALUES (@Key, @Data, @Version, @ETag, CURRENT_TIMESTAMP)
                ON CONFLICT([Key]) DO UPDATE SET
                    [Data] = @Data,
                    [Version] = @Version,
                    [ETag] = @ETag,
                    [UpdatedAt] = CURRENT_TIMESTAMP";

            await using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@Key", key);
            command.Parameters.AddWithValue("@Data", json);
            command.Parameters.AddWithValue("@Version", entity.Version);
            command.Parameters.AddWithValue("@ETag", etag);
            command.CommandTimeout = this.settings.CommandTimeout;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task SaveManyAsync(IEnumerable<KeyValuePair<string, T>> entities, CancellationToken cancellationToken = default)
        {
            await this.EnsureInitializedAsync(cancellationToken);

            var entityList = entities.ToList();
            if (entityList.Count == 0)
            {
                return;
            }

            await using var connection = new SqliteConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                var sql = $@"
                    INSERT INTO [{this.tableName}] ([Key], [Data], [Version], [ETag], [UpdatedAt])
                    VALUES (@Key, @Data, @Version, @ETag, CURRENT_TIMESTAMP)
                    ON CONFLICT([Key]) DO UPDATE SET
                        [Data] = @Data,
                        [Version] = @Version,
                        [ETag] = @ETag,
                        [UpdatedAt] = CURRENT_TIMESTAMP";

                foreach (var kvp in entityList)
                {
                    var json = JsonConvert.SerializeObject(kvp.Value);
                    var etag = Guid.NewGuid().ToString();

                    await using var command = new SqliteCommand(sql, connection, (SqliteTransaction)transaction);
                    command.Parameters.AddWithValue("@Key", kvp.Key);
                    command.Parameters.AddWithValue("@Data", json);
                    command.Parameters.AddWithValue("@Version", kvp.Value.Version);
                    command.Parameters.AddWithValue("@ETag", etag);
                    command.CommandTimeout = this.settings.CommandTimeout;

                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
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

            await using var connection = new SqliteConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqliteCommand($"DELETE FROM [{this.tableName}] WHERE [Key] = @Key", connection);
            command.Parameters.AddWithValue("@Key", key);
            command.CommandTimeout = this.settings.CommandTimeout;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            await this.EnsureInitializedAsync(cancellationToken);

            await using var connection = new SqliteConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqliteCommand($"SELECT COUNT(*) FROM [{this.tableName}] WHERE [Key] = @Key", connection);
            command.Parameters.AddWithValue("@Key", key);
            command.CommandTimeout = this.settings.CommandTimeout;

            var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            return count > 0;
        }

        public async Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            await this.EnsureInitializedAsync(cancellationToken);

            if (predicate == null)
            {
                // Simple count without predicate
                await using var connection = new SqliteConnection(this.connectionString);
                await connection.OpenAsync(cancellationToken);

                await using var command = new SqliteCommand($"SELECT COUNT(*) FROM [{this.tableName}]", connection);
                command.CommandTimeout = this.settings.CommandTimeout;

                return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            }
            else
            {
                // Count with predicate - load all and filter in memory
                var all = await this.GetAllAsync(predicate, cancellationToken);
                return all.Count();
            }
        }

        public async Task<long> ClearAsync(CancellationToken cancellationToken = default)
        {
            await this.EnsureInitializedAsync(cancellationToken);

            await using var connection = new SqliteConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);

            // Get count before clearing
            await using var countCommand = new SqliteCommand($"SELECT COUNT(*) FROM [{this.tableName}]", connection);
            countCommand.CommandTimeout = this.settings.CommandTimeout;
            var count = Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken));

            // Clear the table
            await using var deleteCommand = new SqliteCommand($"DELETE FROM [{this.tableName}]", connection);
            deleteCommand.CommandTimeout = this.settings.CommandTimeout;
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);

            this.logger.LogInformation("Cleared {Count} rows from table {TableName}", count, this.tableName);
            return count;
        }

        public void Dispose()
        {
            this.initLock.Dispose();

            // SQLite connections are automatically pooled and disposed by the connection string's cache mode
            // Force clearing the connection pool for this database
            using var connection = new SqliteConnection(this.connectionString);
            SqliteConnection.ClearPool(connection);
        }
    }
}