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

    /// <summary>
    /// ESENT-based storage provider with automatic crash recovery support.
    /// This provider handles:
    /// - Automatic recovery from dirty shutdowns
    /// - Corrupted database backup and recreation
    /// - Transaction log cleanup
    /// - Temporary file management
    /// - Clean shutdown procedures
    /// </summary>
    /// <typeparam name="T">The entity type to store.</typeparam>
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

                // Perform crash recovery if needed
                if (this.storeSettings.EnableCrashRecovery)
                {
                    this.PerformCrashRecovery(directory);
                }

                // Initialize instance with recovery
                try
                {
                    // JetInit will automatically perform recovery if needed
                    this.instance.Init();
                }
                catch (EsentErrorException ex)
                {
                    this.logger.LogWarning(ex, "ESENT initialization failed, attempting recovery");
                    
                    // Try to recover from dirty shutdown
                    if (ex.Error == JET_err.DatabaseDirtyShutdown || 
                        ex.Error == JET_err.LogFileSizeMismatch ||
                        ex.Error == JET_err.LogFileCorrupt)
                    {
                        this.CleanupCorruptedDatabase(directory);
                        
                        // Recreate instance after cleanup
                        this.instance.Dispose();
                        this.instance = new Instance(this.storeSettings.InstanceName);
                        this.SetInstanceParameters(directory);
                        this.instance.Init();
                    }
                    else
                    {
                        throw;
                    }
                }

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

        private void PerformCrashRecovery(string directory)
        {
            try
            {
                // Check for existing log files that might indicate a dirty shutdown
                var logFiles = Directory.GetFiles(directory, "*.log", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(directory, "edb*.jrs", SearchOption.TopDirectoryOnly))
                    .ToArray();

                if (logFiles.Length > 0)
                {
                    this.logger.LogInformation("Found {Count} ESENT log files, checking for dirty shutdown", logFiles.Length);
                    
                    // Check for checkpoint file
                    var checkpointFile = Path.Combine(directory, "edb.chk");
                    if (!File.Exists(checkpointFile))
                    {
                        this.logger.LogWarning("No checkpoint file found, database may have shut down improperly");
                    }
                }

                // Clean up old temporary files
                this.CleanupTemporaryFiles(directory);
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "Error during crash recovery check");
            }
        }

        private void CleanupCorruptedDatabase(string directory)
        {
            this.logger.LogWarning("Attempting to clean up corrupted database files in {Directory}", directory);

            try
            {
                // Delete transaction logs
                foreach (var logFile in Directory.GetFiles(directory, "*.log", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        File.Delete(logFile);
                        this.logger.LogDebug("Deleted log file: {File}", logFile);
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogWarning(ex, "Failed to delete log file: {File}", logFile);
                    }
                }

                // Delete checkpoint file
                var checkpointFile = Path.Combine(directory, "edb.chk");
                if (File.Exists(checkpointFile))
                {
                    try
                    {
                        File.Delete(checkpointFile);
                        this.logger.LogDebug("Deleted checkpoint file");
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogWarning(ex, "Failed to delete checkpoint file");
                    }
                }

                // Delete reserve logs
                foreach (var reserveLog in Directory.GetFiles(directory, "edbres*.jrs", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        File.Delete(reserveLog);
                        this.logger.LogDebug("Deleted reserve log: {File}", reserveLog);
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogWarning(ex, "Failed to delete reserve log: {File}", reserveLog);
                    }
                }

                // Delete temporary files
                foreach (var tempFile in Directory.GetFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        File.Delete(tempFile);
                        this.logger.LogDebug("Deleted temp file: {File}", tempFile);
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogWarning(ex, "Failed to delete temp file: {File}", tempFile);
                    }
                }

                // If database file exists and is corrupted, we may need to delete it
                if (File.Exists(this.storeSettings.DatabasePath))
                {
                    this.logger.LogWarning("Database file exists but may be corrupted. Backing up before deletion");
                    
                    var backupPath = this.storeSettings.DatabasePath + ".corrupted." + DateTime.UtcNow.Ticks;
                    try
                    {
                        File.Move(this.storeSettings.DatabasePath, backupPath);
                        this.logger.LogInformation("Backed up corrupted database to: {BackupPath}", backupPath);
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogError(ex, "Failed to backup corrupted database");
                        // Last resort - delete the corrupted database
                        File.Delete(this.storeSettings.DatabasePath);
                        this.logger.LogWarning("Deleted corrupted database file");
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to cleanup corrupted database");
                throw new InvalidOperationException("Failed to cleanup corrupted database", ex);
            }
        }

        private void CleanupTemporaryFiles(string directory)
        {
            try
            {
                // Clean up old temporary files
                var tempFiles = Directory.GetFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly)
                    .Where(f => File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-this.storeSettings.TempFileRetentionDays));

                foreach (var tempFile in tempFiles)
                {
                    try
                    {
                        File.Delete(tempFile);
                        this.logger.LogDebug("Deleted old temp file: {File}", tempFile);
                    }
                    catch
                    {
                        // Ignore failures for individual files
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(ex, "Error cleaning up temporary files");
            }
        }

        private void SetInstanceParameters(string directory)
        {
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
        }

        public void Dispose()
        {
            if (!this.disposed)
            {
                if (this.instance != null)
                {
                    try
                    {
                        // Ensure all sessions are closed if using session pool
                        if (this.sessionPool != null)
                        {
                            // Wait for any active sessions to complete
                            this.semaphore.Wait(TimeSpan.FromSeconds(5));
                        }

                        // Detach database before disposing instance
                        using (var session = new Session(this.instance))
                        {
                            try
                            {
                                Api.JetDetachDatabase(session, this.storeSettings.DatabasePath);
                                this.logger.LogDebug("Successfully detached database");
                            }
                            catch (EsentErrorException ex)
                            {
                                this.logger.LogWarning(ex, "Failed to detach database during disposal");
                            }
                        }

                        // Terminate instance cleanly
                        try
                        {
                            this.instance.Term();
                            this.logger.LogDebug("Successfully terminated ESENT instance");
                        }
                        catch (EsentErrorException ex)
                        {
                            this.logger.LogWarning(ex, "Failed to terminate instance cleanly");
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogError(ex, "Error during ESENT provider disposal");
                    }
                    finally
                    {
                        this.instance.Dispose();
                    }
                }
                
                this.semaphore?.Dispose();
                this.disposed = true;
                this.logger.LogInformation("ESENT provider disposed for entity type {EntityType}", typeof(T).Name);
            }
        }
    }
}