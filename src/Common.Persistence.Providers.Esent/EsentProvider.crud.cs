//-------------------------------------------------------------------------------
// <copyright file="EsentProvider.crud.cs" company="Microsoft Corp.">
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
    using Microsoft.Isam.Esent.Interop;
    using Unity;

    public partial class EsentProvider<T> : ICrudStorageProvider<T> where T : IEntity
    {
        private readonly EsentStoreSettings storeSettings;
        private readonly ILogger<EsentProvider<T>> logger;
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
        private Instance instance;
        private Session session;
        private JET_DBID dbid;
        private Table table;
        private JET_COLUMNID keyColumn;
        private JET_COLUMNID dataColumn;
        private JET_COLUMNID versionColumn;
        private readonly string tableName;

        public EsentProvider(IServiceProvider serviceProvider, string name)
            : base(serviceProvider, name)
        {
            this.storeSettings = this.ConfigReader.ReadSettings<EsentStoreSettings>();
            this.logger = this.GetLogger<EsentProvider<T>>();
            this.tableName = typeof(T).Name;
            this.InitializeDatabase();
        }

        public EsentProvider(UnityContainer container, string name)
            : base(container, name)
        {
            this.storeSettings = this.ConfigReader.ReadSettings<EsentStoreSettings>();
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

                // Create session
                this.session = new Session(this.instance);

                // Create or open database
                if (!File.Exists(this.storeSettings.DatabasePath))
                {
                    Api.JetCreateDatabase(this.session, this.storeSettings.DatabasePath, null, out this.dbid, CreateDatabaseGrbit.OverwriteExisting);
                    this.CreateTable();
                }
                else
                {
                    Api.JetAttachDatabase(this.session, this.storeSettings.DatabasePath, AttachDatabaseGrbit.None);
                    Api.JetOpenDatabase(this.session, this.storeSettings.DatabasePath, null, out this.dbid, OpenDatabaseGrbit.None);
                    this.OpenTable();
                }

                this.logger.LogInformation("ESENT database initialized at {DatabasePath} for entity type {EntityType}", 
                    this.storeSettings.DatabasePath, typeof(T).Name);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to initialize ESENT database");
                throw;
            }
        }

        private void CreateTable()
        {
            using (var transaction = new Transaction(this.session))
            {
                JET_TABLEID tableid;
                Api.JetCreateTable(this.session, this.dbid, this.tableName, 0, 100, out tableid);

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
                Api.JetAddColumn(this.session, tableid, "Key", keyColumnDef, null, 0, out this.keyColumn);
                Api.JetAddColumn(this.session, tableid, "Data", dataColumnDef, null, 0, out this.dataColumn);
                Api.JetAddColumn(this.session, tableid, "Version", versionColumnDef, null, 0, out this.versionColumn);

                // Create primary index on Key
                var primaryIndexDef = "+Key\0\0";
                Api.JetCreateIndex(this.session, tableid, "PrimaryIndex", CreateIndexGrbit.IndexPrimary | CreateIndexGrbit.IndexUnique, 
                    primaryIndexDef, primaryIndexDef.Length, 100);

                Api.JetCloseTable(this.session, tableid);
                transaction.Commit(CommitTransactionGrbit.None);
            }

            this.OpenTable();
        }

        private void OpenTable()
        {
            this.table = new Table(this.session, this.dbid, this.tableName, OpenTableGrbit.None);
            
            // Get column IDs
            this.keyColumn = Api.GetTableColumnid(this.session, this.table, "Key");
            this.dataColumn = Api.GetTableColumnid(this.session, this.table, "Data");
            this.versionColumn = Api.GetTableColumnid(this.session, this.table, "Version");
        }

        public async Task<T?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            await this.semaphore.WaitAsync(cancellationToken);
            try
            {
                return await Task.Run(() =>
                {
                    using (var transaction = new Transaction(this.session))
                    {
                        Api.JetSetCurrentIndex(this.session, this.table, "PrimaryIndex");
                        Api.MakeKey(this.session, this.table, key, Encoding.Unicode, MakeKeyGrbit.NewKey);
                        
                        if (Api.TrySeek(this.session, this.table, SeekGrbit.SeekEQ))
                        {
                            var data = Api.RetrieveColumn(this.session, this.table, this.dataColumn);
                            if (data != null)
                            {
                                return this.Serializer.DeserializeAsync(data, cancellationToken).Result;
                            }
                        }
                        
                        return default(T);
                    }
                }, cancellationToken);
            }
            finally
            {
                this.semaphore.Release();
            }
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
            await this.semaphore.WaitAsync(cancellationToken);
            try
            {
                return await Task.Run(() =>
                {
                    var results = new List<T>();
                    
                    using (var transaction = new Transaction(this.session))
                    {
                        Api.JetSetCurrentIndex(this.session, this.table, null); // Use primary index
                        
                        if (Api.TryMoveFirst(this.session, this.table))
                        {
                            do
                            {
                                var data = Api.RetrieveColumn(this.session, this.table, this.dataColumn);
                                if (data != null)
                                {
                                    var entity = this.Serializer.DeserializeAsync(data, cancellationToken).Result;
                                    if (entity != null)
                                    {
                                        if (predicate == null || predicate.Compile()(entity))
                                        {
                                            results.Add(entity);
                                        }
                                    }
                                }
                            }
                            while (Api.TryMoveNext(this.session, this.table));
                        }
                    }
                    
                    return results;
                }, cancellationToken);
            }
            finally
            {
                this.semaphore.Release();
            }
        }

        public async Task SaveAsync(string key, T entity, CancellationToken cancellationToken = default)
        {
            await this.semaphore.WaitAsync(cancellationToken);
            try
            {
                await Task.Run(async () =>
                {
                    using (var transaction = new Transaction(this.session))
                    {
                        Api.JetSetCurrentIndex(this.session, this.table, "PrimaryIndex");
                        Api.MakeKey(this.session, this.table, key, Encoding.Unicode, MakeKeyGrbit.NewKey);
                        
                        var data = await this.Serializer.SerializeAsync(entity, cancellationToken);
                        
                        if (Api.TrySeek(this.session, this.table, SeekGrbit.SeekEQ))
                        {
                            // Update existing record
                            using (var update = new Update(this.session, this.table, JET_prep.Replace))
                            {
                                Api.SetColumn(this.session, this.table, this.dataColumn, data);
                                update.Save();
                            }
                        }
                        else
                        {
                            // Insert new record
                            using (var update = new Update(this.session, this.table, JET_prep.Insert))
                            {
                                Api.SetColumn(this.session, this.table, this.keyColumn, key, Encoding.Unicode);
                                Api.SetColumn(this.session, this.table, this.dataColumn, data);
                                update.Save();
                            }
                        }
                        
                        transaction.Commit(CommitTransactionGrbit.None);
                    }
                }, cancellationToken);
                
                this.logger.LogDebug("Saved entity with key {Key} to ESENT database", key);
            }
            finally
            {
                this.semaphore.Release();
            }
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
            await this.semaphore.WaitAsync(cancellationToken);
            try
            {
                await Task.Run(() =>
                {
                    using (var transaction = new Transaction(this.session))
                    {
                        Api.JetSetCurrentIndex(this.session, this.table, "PrimaryIndex");
                        Api.MakeKey(this.session, this.table, key, Encoding.Unicode, MakeKeyGrbit.NewKey);
                        
                        if (Api.TrySeek(this.session, this.table, SeekGrbit.SeekEQ))
                        {
                            Api.JetDelete(this.session, this.table);
                            transaction.Commit(CommitTransactionGrbit.None);
                            this.logger.LogDebug("Deleted entity with key {Key} from ESENT database", key);
                        }
                    }
                }, cancellationToken);
            }
            finally
            {
                this.semaphore.Release();
            }
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            await this.semaphore.WaitAsync(cancellationToken);
            try
            {
                return await Task.Run(() =>
                {
                    using (var transaction = new Transaction(this.session))
                    {
                        Api.JetSetCurrentIndex(this.session, this.table, "PrimaryIndex");
                        Api.MakeKey(this.session, this.table, key, Encoding.Unicode, MakeKeyGrbit.NewKey);
                        return Api.TrySeek(this.session, this.table, SeekGrbit.SeekEQ);
                    }
                }, cancellationToken);
            }
            finally
            {
                this.semaphore.Release();
            }
        }

        public async Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            var entities = await this.GetAllAsync(predicate, cancellationToken);
            return entities.Count();
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

        public void Dispose()
        {
            this.table?.Dispose();
            this.session?.Dispose();
            this.instance?.Dispose();
            this.semaphore?.Dispose();
        }
    }
}