//-------------------------------------------------------------------------------
// <copyright file="EsentProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.Esent
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Persistence.Contract;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.ObjectPool;
    using Microsoft.Isam.Esent.Interop;
    using Unity;

    public partial class EsentProvider<T> : ICrudStorageProvider<T> where T : IEntity
    {
        private readonly EsentStoreSettings storeSettings;
        private readonly ILogger<EsentProvider<T>> logger;
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
        private Instance instance;
        private ObjectPool<Session>? sessionPool;
        private readonly string tableName;
        private bool disposed;

        public EsentProvider(IServiceProvider serviceProvider, string name)
            : base(serviceProvider, name)
        {
            this.storeSettings = this.ConfigReader.ReadSettings<EsentStoreSettings>(name);
            this.logger = this.GetLogger<EsentProvider<T>>();
            this.tableName = typeof(T).Name;
            this.InitializeDatabase();
        }

        public EsentProvider(UnityContainer container, string name)
            : base(container, name)
        {
            this.storeSettings = this.ConfigReader.ReadSettings<EsentStoreSettings>(name);
            this.logger = this.GetLogger<EsentProvider<T>>();
            this.tableName = typeof(T).Name;
            this.InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            try
            {
                this.EnsureDirectoryExists();

                // Initialize ESENT instance
                this.instance = new Instance(this.storeSettings.InstanceName);

                // Set instance parameters
                var directory = Path.GetDirectoryName(this.storeSettings.DatabasePath) ?? "data";
                this.instance.Parameters.SystemDirectory = directory;
                this.instance.Parameters.TempDirectory = directory;
                this.instance.Parameters.LogFileDirectory = directory;
                this.instance.Parameters.BaseName = "edb";
                this.instance.Parameters.NoInformationEvent = true;
                this.instance.Parameters.CircularLog = true;
                this.instance.Parameters.MaxSessions = 256;
                this.instance.Parameters.MaxOpenTables = 256;
                this.instance.Parameters.MaxCursors = 1024;
                this.instance.Parameters.MaxVerPages = 1024;

                // Initialize instance
                this.instance.Init();

                // Initialize session pool if enabled
                if (this.storeSettings.UseSessionPool)
                {
                    var provider = new DefaultObjectPoolProvider();
                    var policy = new SessionPooledObjectPolicy(this.instance);
                    this.sessionPool = provider.Create(policy);
                }

                // Create or open database
                using (var session = new Session(this.instance))
                {
                    if (!File.Exists(this.storeSettings.DatabasePath))
                    {
                        Api.JetCreateDatabase(session, this.storeSettings.DatabasePath, null, out var dbid, CreateDatabaseGrbit.OverwriteExisting);
                        this.CreateTable(session, dbid);
                        Api.JetCloseDatabase(session, dbid, CloseDatabaseGrbit.None);
                    }
                    else
                    {
                        Api.JetAttachDatabase(session, this.storeSettings.DatabasePath, AttachDatabaseGrbit.None);
                        Api.JetOpenDatabase(session, this.storeSettings.DatabasePath, null, out var dbid, OpenDatabaseGrbit.None);
                        
                        // Check if table exists, create if not
                        if (!this.TableExists(session, dbid))
                        {
                            this.CreateTable(session, dbid);
                        }
                        
                        Api.JetCloseDatabase(session, dbid, CloseDatabaseGrbit.None);
                    }
                }

                this.logger.LogInformation("ESENT database initialized at {DatabasePath} for entity type {EntityType}",
                    this.storeSettings.DatabasePath, typeof(T).Name);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to initialize ESENT database");
                throw new InvalidOperationException($"Failed to initialize ESENT database: {ex.Message}", ex);
            }
        }

        private bool TableExists(Session session, JET_DBID dbid)
        {
            try
            {
                using var table = new Table(session, dbid, this.tableName, OpenTableGrbit.None);
                return true;
            }
            catch (EsentObjectNotFoundException)
            {
                return false;
            }
        }

        private void CreateTable(Session session, JET_DBID dbid)
        {
            using (var transaction = new Transaction(session))
            {
                JET_TABLEID tableid;
                Api.JetCreateTable(session, dbid, this.tableName, 0, 100, out tableid);

                // Define columns
                var keyColumnDef = new JET_COLUMNDEF
                {
                    coltyp = JET_coltyp.LongText,
                    cp = JET_CP.Unicode,
                    grbit = ColumndefGrbit.ColumnNotNULL
                };

                var dataColumnDef = new JET_COLUMNDEF
                {
                    coltyp = JET_coltyp.LongBinary,
                    grbit = ColumndefGrbit.None
                };

                var versionColumnDef = new JET_COLUMNDEF
                {
                    coltyp = JET_coltyp.Currency,
                    grbit = ColumndefGrbit.ColumnAutoincrement | ColumndefGrbit.ColumnNotNULL
                };

                // Create columns
                JET_COLUMNID keyColumn, dataColumn, versionColumn;
                Api.JetAddColumn(session, tableid, "Key", keyColumnDef, null, 0, out keyColumn);
                Api.JetAddColumn(session, tableid, "Data", dataColumnDef, null, 0, out dataColumn);
                Api.JetAddColumn(session, tableid, "Version", versionColumnDef, null, 0, out versionColumn);

                // Create primary index on Key
                var primaryIndexDef = "+Key\0\0";
                Api.JetCreateIndex(session, tableid, "PrimaryIndex", CreateIndexGrbit.IndexPrimary | CreateIndexGrbit.IndexUnique,
                    primaryIndexDef, primaryIndexDef.Length, 100);

                Api.JetCloseTable(session, tableid);
                transaction.Commit(CommitTransactionGrbit.None);
            }
        }

        public async Task<T?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();
            
            return await this.ExecuteWithSessionAsync(session =>
            {
                Api.JetOpenDatabase(session, this.storeSettings.DatabasePath, null, out var db, OpenDatabaseGrbit.None);
                
                try
                {
                    // Ensure table exists
                    this.EnsureTableExists(session, db);
                    
                    using var transaction = new Transaction(session);
                    using var table = new Table(session, db, this.tableName, OpenTableGrbit.None);
                    
                    var keyColumn = Api.GetTableColumnid(session, table, "Key");
                    var dataColumn = Api.GetTableColumnid(session, table, "Data");
                    
                    Api.JetSetCurrentIndex(session, table, "PrimaryIndex");
                    Api.MakeKey(session, table, key, Encoding.Unicode, MakeKeyGrbit.NewKey);
                    
                    if (Api.TrySeek(session, table, SeekGrbit.SeekEQ))
                    {
                        var data = Api.RetrieveColumn(session, table, dataColumn);
                        if (data != null)
                        {
                            // Use synchronous deserialization or handle async properly
                            return this.Serializer.DeserializeAsync(data, cancellationToken).GetAwaiter().GetResult();
                        }
                    }
                    
                    return default(T);
                }
                finally
                {
                    Api.JetCloseDatabase(session, db, CloseDatabaseGrbit.None);
                }
            }, cancellationToken);
        }

        public async Task<IEnumerable<T>> GetManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            var results = new List<T>();

            foreach (var key in keys)
            {
                var entity = await this.GetAsync(key, cancellationToken);
                if (entity != null)
                {
                    results.Add(entity);
                }
            }

            return results;
        }

        public async Task<IEnumerable<T>> GetAllAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();
            
            return await this.ExecuteWithSessionAsync(session =>
            {
                var results = new List<T>();

                Api.JetOpenDatabase(session, this.storeSettings.DatabasePath, null, out var db, OpenDatabaseGrbit.None);
                
                try
                {
                    // Ensure table exists
                    this.EnsureTableExists(session, db);
                    
                    using var transaction = new Transaction(session);
                    using var table = new Table(session, db, this.tableName, OpenTableGrbit.None);
                    
                    var dataColumn = Api.GetTableColumnid(session, table, "Data");
                    
                    Api.JetSetCurrentIndex(session, table, null); // Use primary index

                    if (Api.TryMoveFirst(session, table))
                    {
                        do
                        {
                            var data = Api.RetrieveColumn(session, table, dataColumn);
                            if (data != null)
                            {
                                var entity = this.Serializer.DeserializeAsync(data, cancellationToken).GetAwaiter().GetResult();
                                if (entity != null)
                                {
                                    if (predicate == null || predicate.Compile()(entity))
                                    {
                                        results.Add(entity);
                                    }
                                }
                            }
                        }
                        while (Api.TryMoveNext(session, table));
                    }
                    
                    return results;
                }
                finally
                {
                    Api.JetCloseDatabase(session, db, CloseDatabaseGrbit.None);
                }
            }, cancellationToken);
        }

        public async Task SaveAsync(string key, T entity, CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();
            
            await this.ExecuteWithSessionAsyncForAsync(async session =>
            {
                return await Task.Run(async () =>
                {
                    Api.JetOpenDatabase(session, this.storeSettings.DatabasePath, null, out var db, OpenDatabaseGrbit.None);
                    
                    try
                    {
                        // Ensure table exists
                        this.EnsureTableExists(session, db);
                        
                        using var transaction = new Transaction(session);
                        using var table = new Table(session, db, this.tableName, OpenTableGrbit.None);
                        
                        var keyColumn = Api.GetTableColumnid(session, table, "Key");
                        var dataColumn = Api.GetTableColumnid(session, table, "Data");
                        
                        Api.JetSetCurrentIndex(session, table, "PrimaryIndex");
                        Api.MakeKey(session, table, key, Encoding.Unicode, MakeKeyGrbit.NewKey);

                        var data = await this.Serializer.SerializeAsync(entity, cancellationToken);

                        if (Api.TrySeek(session, table, SeekGrbit.SeekEQ))
                        {
                            // Update existing record
                            using (var update = new Update(session, table, JET_prep.Replace))
                            {
                                Api.SetColumn(session, table, dataColumn, data);
                                update.Save();
                            }
                        }
                        else
                        {
                            // Insert new record
                            using (var update = new Update(session, table, JET_prep.Insert))
                            {
                                Api.SetColumn(session, table, keyColumn, key, Encoding.Unicode);
                                Api.SetColumn(session, table, dataColumn, data);
                                update.Save();
                            }
                        }

                        transaction.Commit(CommitTransactionGrbit.None);
                        return 0; // dummy return
                    }
                    finally
                    {
                        Api.JetCloseDatabase(session, db, CloseDatabaseGrbit.None);
                    }
                }, cancellationToken);
            }, cancellationToken);

            this.logger.LogDebug("Saved entity with key {Key} to ESENT database", key);
        }

        public async Task SaveManyAsync(IEnumerable<KeyValuePair<string, T>> entities, CancellationToken cancellationToken = default)
        {
            foreach (var (key, entity) in entities)
            {
                await this.SaveAsync(key, entity, cancellationToken);
            }
        }

        public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();
            
            await this.ExecuteWithSessionAsync(session =>
            {
                Api.JetOpenDatabase(session, this.storeSettings.DatabasePath, null, out var db, OpenDatabaseGrbit.None);
                
                try
                {
                    // Ensure table exists
                    this.EnsureTableExists(session, db);
                    
                    using var transaction = new Transaction(session);
                    using var table = new Table(session, db, this.tableName, OpenTableGrbit.None);
                    
                    var keyColumn = Api.GetTableColumnid(session, table, "Key");
                    
                    Api.JetSetCurrentIndex(session, table, "PrimaryIndex");
                    Api.MakeKey(session, table, key, Encoding.Unicode, MakeKeyGrbit.NewKey);

                    if (Api.TrySeek(session, table, SeekGrbit.SeekEQ))
                    {
                        Api.JetDelete(session, table);
                        transaction.Commit(CommitTransactionGrbit.None);
                        this.logger.LogDebug("Deleted entity with key {Key} from ESENT database", key);
                    }
                }
                finally
                {
                    Api.JetCloseDatabase(session, db, CloseDatabaseGrbit.None);
                }
            }, cancellationToken);
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();
            
            return await this.ExecuteWithSessionAsync(session =>
            {
                Api.JetOpenDatabase(session, this.storeSettings.DatabasePath, null, out var db, OpenDatabaseGrbit.None);
                
                try
                {
                    // Ensure table exists
                    this.EnsureTableExists(session, db);
                    
                    using var transaction = new Transaction(session);
                    using var table = new Table(session, db, this.tableName, OpenTableGrbit.None);
                    
                    Api.JetSetCurrentIndex(session, table, "PrimaryIndex");
                    Api.MakeKey(session, table, key, Encoding.Unicode, MakeKeyGrbit.NewKey);
                    return Api.TrySeek(session, table, SeekGrbit.SeekEQ);
                }
                finally
                {
                    Api.JetCloseDatabase(session, db, CloseDatabaseGrbit.None);
                }
            }, cancellationToken);
        }

        public async Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            var entities = await this.GetAllAsync(predicate, cancellationToken);
            return entities.Count();
        }

        public async Task<long> ClearAsync(CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();
            
            return await this.ExecuteWithSessionAsync(session =>
            {
                Api.JetOpenDatabase(session, this.storeSettings.DatabasePath, null, out var db, OpenDatabaseGrbit.None);
                
                try
                {
                    // Ensure table exists before trying to clear it
                    this.EnsureTableExists(session, db);
                    
                    long count = 0;
                    using (var transaction = new Transaction(session))
                    {
                        using (var table = new Table(session, db, this.tableName, OpenTableGrbit.None))
                        {
                            // Count records before deletion
                            if (Api.TryMoveFirst(session, table))
                            {
                                do
                                {
                                    count++;
                                }
                                while (Api.TryMoveNext(session, table));
                            }
                        }
                        
                        // Delete and recreate the table
                        Api.JetDeleteTable(session, db, this.tableName);
                        transaction.Commit(CommitTransactionGrbit.None);
                    }
                    
                    // Recreate the table
                    this.CreateTable(session, db);
                    
                    this.logger.LogDebug("Cleared {Count} entities from ESENT database", count);
                    return count;
                }
                finally
                {
                    Api.JetCloseDatabase(session, db, CloseDatabaseGrbit.None);
                }
            }, cancellationToken);
        }

        private async Task<TResult> ExecuteWithSessionAsync<TResult>(
            Func<Session, TResult> operation,
            CancellationToken cancellationToken = default)
        {
            if (this.storeSettings.UseSessionPool && this.sessionPool != null)
            {
                var session = this.sessionPool.Get();
                try
                {
                    await this.semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        return await Task.Run(() => operation(session), cancellationToken);
                    }
                    finally
                    {
                        this.semaphore.Release();
                    }
                }
                finally
                {
                    this.sessionPool.Return(session);
                }
            }
            else
            {
                await this.semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await Task.Run(() =>
                    {
                        using var session = new Session(this.instance);
                        return operation(session);
                    }, cancellationToken);
                }
                finally
                {
                    this.semaphore.Release();
                }
            }
        }

        private async Task ExecuteWithSessionAsync(
            Action<Session> operation,
            CancellationToken cancellationToken = default)
        {
            await ExecuteWithSessionAsync(session =>
            {
                operation(session);
                return 0; // dummy return value
            }, cancellationToken);
        }

        private async Task<TResult> ExecuteWithSessionAsyncForAsync<TResult>(
            Func<Session, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            if (this.storeSettings.UseSessionPool && this.sessionPool != null)
            {
                var session = this.sessionPool.Get();
                try
                {
                    await this.semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        return await operation(session);
                    }
                    finally
                    {
                        this.semaphore.Release();
                    }
                }
                finally
                {
                    this.sessionPool.Return(session);
                }
            }
            else
            {
                await this.semaphore.WaitAsync(cancellationToken);
                try
                {
                    using var session = new Session(this.instance);
                    return await operation(session);
                }
                finally
                {
                    this.semaphore.Release();
                }
            }
        }

        private void EnsureDirectoryExists()
        {
            var directory = Path.GetDirectoryName(this.storeSettings.DatabasePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                this.logger.LogDebug("Created directory {Directory}", directory);
            }
        }

        private void EnsureTableExists(Session session, JET_DBID dbid)
        {
            if (!this.TableExists(session, dbid))
            {
                this.logger.LogDebug("Table {TableName} does not exist, creating it", this.tableName);
                this.CreateTable(session, dbid);
            }
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(EsentProvider<T>));
            }
        }

        public void Dispose()
        {
            if (!this.disposed)
            {
                if (this.instance != null)
                {
                    try
                    {
                        // Detach database before disposing instance
                        using (var session = new Session(this.instance))
                        {
                            Api.JetDetachDatabase(session, this.storeSettings.DatabasePath);
                        }
                    }
                    catch
                    {
                        // Ignore errors during cleanup
                    }
                    
                    this.instance.Dispose();
                }
                
                this.semaphore?.Dispose();
                this.disposed = true;
            }
        }
    }
}