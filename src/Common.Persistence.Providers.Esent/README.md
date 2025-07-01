# ESENT Persistence Provider

## Overview

The ESENT (Extensible Storage Engine) persistence provider offers high-performance, transactional storage using Microsoft's native Windows database engine. ESENT has been part of Windows since Windows 2000 and powers critical applications like Active Directory, Exchange Server, and Windows Search.

## Features

- **ACID Transactions**: Full transactional support with automatic rollback on failure
- **High Performance**: Native Windows integration with minimal overhead
- **Zero Configuration**: No separate database server or runtime required
- **Crash Recovery**: Automatic database recovery after unexpected shutdowns
- **Thread Safety**: Built-in concurrency control with row-level locking
- **Compact Storage**: Efficient binary format with compression support
- **Streaming Support**: Efficient handling of large binary data
- **Multi-version Concurrency**: Optimistic concurrency control
- **Database Encryption**: Optional database-level encryption
- **Hot Backup**: Online backup without service interruption

## Usage

### Basic Configuration

```csharp
var settings = new EsentStoreSettings
{
    DatabasePath = "data/myapp.edb",
    InstanceName = "MyAppInstance",
    CacheSizeMB = 64,
    MaxDatabaseSizeMB = 1024,
    EnableVersioning = true,
    PageSizeKB = 8
};

// Simple usage (without dependency injection)
var provider = new SimpleEsentProvider<Product>(settings);

// Full provider (with DI and full persistence framework)
services.AddSingleton<ICrudStorageProvider<Product>>(sp =>
    new EsentProvider<Product>(sp, "ProductStore"));
```

### With Transactions

```csharp
using var transaction = _transactionFactory.CreateTransaction();
var resource = new TransactionalResource<Product>(provider);
transaction.EnlistResource(resource);

var product = new Product { Id = "12345", Name = "Widget", Price = 29.99m };
await resource.SaveEntity(product.Key, product);
await transaction.CommitAsync();
```

### Configuration Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DatabasePath` | string | `"data/esent.db"` | Path to the database file |
| `InstanceName` | string | `"ReliableStore"` | Unique name for the ESENT instance |
| `CacheSizeMB` | int | `64` | Memory cache size in megabytes |
| `MaxDatabaseSizeMB` | int | `1024` | Maximum database size in megabytes |
| `EnableVersioning` | bool | `true` | Enable automatic versioning |
| `PageSizeKB` | int | `8` | Database page size (2, 4, 8, 16, or 32 KB) |
| `EnableLogging` | bool | `true` | Enable transaction logging |
| `LogFileDirectory` | string | Same as DB | Directory for transaction logs |
| `CheckpointSizeMB` | int | `32` | Checkpoint threshold size |
| `CircularLogging` | bool | `false` | Enable circular logging (no point-in-time recovery) |
| `EnableCompression` | bool | `true` | Enable data compression |
| `MaxSessions` | int | `256` | Maximum concurrent sessions |
| `MaxTables` | int | `1024` | Maximum number of tables |
| `TempPath` | string | System temp | Path for temporary database files |

## Platform Requirements

**⚠️ Windows Only**: ESENT is a Windows-specific database engine and requires:

- Windows operating system (Windows 2000 or later)
- .NET 9.0 or later
- The `esent.dll` system library (included with Windows)

### Linux/macOS Compatibility

ESENT is **not available** on Linux or macOS. If you need cross-platform support, consider using:

- `Common.Persistence.Providers.FileSystem` - JSON file-based storage
- `Common.Persistence.Providers.InMemory` - In-memory storage
- Third-party providers like SQLite, PostgreSQL, or MongoDB

## Performance Characteristics

### Strengths
- **Fast Reads**: Sub-millisecond indexed lookups
- **Efficient Storage**: Binary format with compression (typically 30-50% size reduction)
- **Low Latency**: Direct system API calls without network overhead
- **Concurrent Access**: Row-level locking supports high concurrency
- **Sequential Writes**: Optimized for append-heavy workloads
- **Batch Operations**: Efficient bulk insert/update capabilities

### Performance Metrics
| Operation | Typical Performance | Notes |
|-----------|-------------------|-------|
| Single Read | < 1ms | Indexed lookup |
| Single Write | 1-5ms | With transaction commit |
| Batch Write (1000 items) | 50-100ms | Amortized cost |
| Full Table Scan | 10-50ms per 10K rows | Depends on row size |
| Index Creation | 100-500ms per 100K rows | One-time cost |

### Considerations
- **Windows Only**: Platform dependency limits deployment options
- **Single Machine**: No built-in clustering or replication
- **Write Amplification**: Each write involves transaction log
- **Database Size**: Performance degrades beyond 10GB
- **Memory Usage**: Cache size directly impacts performance

## Schema Design

The ESENT provider automatically creates a table for each entity type with the following structure:

```
Table: {EntityTypeName}
├── Key (Primary Key, Unicode Text)
├── Data (Binary, JSON-serialized entity)
└── Version (Auto-increment, Long)
```

## Error Handling

Common scenarios and their handling:

- **Database Corruption**: Automatic recovery on startup
- **Disk Full**: Graceful failure with descriptive exception
- **Concurrent Access**: Built-in locking prevents conflicts
- **Process Termination**: Transaction log ensures consistency

## Monitoring and Debugging

Enable detailed logging by configuring the logger:

```csharp
services.AddLogging(builder =>
{
    builder.SetMinimumLevel(LogLevel.Debug);
    builder.AddConsole();
});
```

Key metrics to monitor:
- Database file size growth
- Transaction duration
- Cache hit ratio
- Concurrent session count

## Migration and Backup

### Database Backup
```csharp
// Backup is handled through the IPersistenceProvider interface
var backupPath = await provider.CreateBackupAsync("backup_20231201.edb");
```

### Data Migration
```csharp
// Use the built-in migration support
await provider.MigrateAsync(fromVersion: 1, toVersion: 2, migrationScript);
```

## Best Practices

1. **Database Sizing**: Keep databases under 4GB for optimal performance
2. **Transaction Scope**: Keep transactions short to minimize lock contention
3. **Indexing**: Use entity properties for filtering in `GetAllAsync` predicates
4. **Cache Management**: Monitor memory usage and adjust cache size accordingly
5. **Backup Strategy**: Implement regular backup schedules for production data

## Troubleshooting

### Common Issues

**Database Lock Errors**
- Ensure proper disposal of provider instances
- Check for orphaned processes holding database locks

**Performance Degradation**
- Monitor database size and consider archiving old data
- Verify adequate disk space and memory allocation
- Check for long-running transactions

**Startup Failures**
- Verify file permissions on database directory
- Ensure ESENT instance names are unique per process
- Check Windows Event Log for detailed error information

## Example: Product Catalog Service

```csharp
// Startup.cs
public void ConfigureServices(IServiceCollection services)
{
    services.Configure<EsentStoreSettings>(Configuration.GetSection("EsentStore"));
    services.AddSingleton<ICrudStorageProvider<Product>>(sp =>
    {
        var settings = sp.GetRequiredService<IOptions<EsentStoreSettings>>().Value;
        return new SimpleEsentProvider<Product>(settings);
    });
}

// ProductService.cs
public class ProductService
{
    private readonly ICrudStorageProvider<Product> _store;

    public async Task<Product?> GetProductAsync(string id)
    {
        return await _store.GetAsync($"Product/{id}");
    }

    public async Task SaveProductAsync(Product product)
    {
        await _store.SaveAsync(product.Key, product);
    }
}
```

## Advanced Usage

### Custom Indexing

```csharp
public class IndexedEsentProvider<T> : EsentProvider<T> where T : class
{
    protected override void ConfigureTable(Table table)
    {
        base.ConfigureTable(table);

        // Add custom index on entity property
        table.CreateIndex("idx_name", "+Name\0", CreateIndexGrbit.IndexUnique);
        table.CreateIndex("idx_created", "+CreatedDate\0", CreateIndexGrbit.None);
    }
}
```

### Performance Tuning

```csharp
// High-throughput configuration
var settings = new EsentStoreSettings
{
    CacheSizeMB = 512,              // Increase cache for better performance
    PageSizeKB = 32,                // Larger pages for sequential access
    CheckpointSizeMB = 128,         // Less frequent checkpoints
    EnableCompression = false,       // Disable for CPU-bound workloads
    CircularLogging = true,         // Better performance, no point-in-time recovery
    MaxSessions = 1024              // Support more concurrent operations
};

// Durability-focused configuration
var settings = new EsentStoreSettings
{
    EnableLogging = true,           // Full transaction logging
    CircularLogging = false,        // Enable point-in-time recovery
    CheckpointSizeMB = 16,          // Frequent checkpoints
    EnableCompression = true,       // Reduce storage footprint
    MaxDatabaseSizeMB = 10240      // Allow larger databases
};
```

### Maintenance Operations

```csharp
public class EsentMaintenanceService
{
    private readonly EsentProvider<T> _provider;

    public async Task CompactDatabaseAsync()
    {
        // Offline defragmentation
        await _provider.StopAsync();
        EsentUtilities.CompactDatabase(_provider.DatabasePath);
        await _provider.StartAsync();
    }

    public async Task<DatabaseStats> GetDatabaseStatsAsync()
    {
        return new DatabaseStats
        {
            SizeMB = new FileInfo(_provider.DatabasePath).Length / 1048576,
            TableCount = await _provider.GetTableCountAsync(),
            RecordCount = await _provider.GetRecordCountAsync(),
            FragmentationPercent = await _provider.GetFragmentationAsync()
        };
    }
}
```

## Security Considerations

### Database Encryption

```csharp
var settings = new EsentStoreSettings
{
    EnableEncryption = true,
    EncryptionKey = Convert.ToBase64String(key),
    EncryptionAlgorithm = "AES256"
};
```

### Access Control
- Use Windows file system permissions to control database access
- Run service accounts with minimal privileges
- Store database files outside web-accessible directories
- Enable audit logging for sensitive operations

## Disaster Recovery

### Backup Strategies

1. **Online Backup** (Recommended)
```csharp
// Streaming backup while database is online
await provider.BackupAsync(backupPath, BackupOptions.Incremental);
```

2. **Offline Backup**
```csharp
// Stop service and copy files
await provider.StopAsync();
File.Copy(databasePath, backupPath);
await provider.StartAsync();
```

3. **Point-in-Time Recovery**
```csharp
// Requires circular logging disabled
await provider.RestoreAsync(backupPath, targetDateTime);
```

### High Availability Options

While ESENT doesn't support native clustering, you can implement HA using:

1. **Active-Passive Failover**: Use Windows Failover Clustering with shared storage
2. **Log Shipping**: Replicate transaction logs to standby server
3. **Application-Level Replication**: Implement custom replication logic

### Distributed Transactions with Optimistic Concurrency

1. Version-Based Optimistic Locking

Use ESENT's auto-incrementing version column to detect concurrent modifications
Store version numbers when acquiring locks
Verify version hasn't changed before releasing locks

2. Distributed Lock Manager

Implement a lock table with version tracking
Support both shared and exclusive locks
Include timeout and expiration mechanisms
Use machine name and process ID for ownership tracking

3. Two-Phase Commit Protocol

Transaction log table tracks state transitions
Each phase is logged with version information
Supports prepare, commit, and rollback phases.

```csharp

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Isam.Esent.Interop;

/// <summary>
/// Handles ESENT database access on shared volumes with proper synchronization
/// </summary>
public class SharedVolumeEsentHandler
{
    private readonly string _databasePath;
    private readonly string _lockFilePath;
    private readonly TimeSpan _lockTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _lockRetryInterval = TimeSpan.FromMilliseconds(100);

    public SharedVolumeEsentHandler(string databasePath)
    {
        _databasePath = databasePath;
        _lockFilePath = Path.Combine(Path.GetDirectoryName(databasePath), $"{Path.GetFileName(databasePath)}.lock");
    }

    /// <summary>
    /// Execute an operation with file-based locking for shared volume access
    /// </summary>
    public async Task<T> ExecuteWithFileLockAsync<T>(Func<Task<T>> operation)
    {
        var lockAcquired = false;
        var startTime = DateTime.UtcNow;
        FileStream lockFileStream = null;

        try
        {
            while (!lockAcquired && DateTime.UtcNow - startTime < _lockTimeout)
            {
                try
                {
                    // Try to create/open lock file exclusively
                    lockFileStream = new FileStream(
                        _lockFilePath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        4096,
                        FileOptions.DeleteOnClose);

                    // Write process information to lock file
                    var lockInfo = $"{Environment.MachineName}|{System.Diagnostics.Process.GetCurrentProcess().Id}|{DateTime.UtcNow}";
                    var bytes = System.Text.Encoding.UTF8.GetBytes(lockInfo);
                    await lockFileStream.WriteAsync(bytes, 0, bytes.Length);
                    await lockFileStream.FlushAsync();

                    lockAcquired = true;
                }
                catch (IOException)
                {
                    // Lock file is in use by another process
                    await Task.Delay(_lockRetryInterval);
                }
            }

            if (!lockAcquired)
            {
                throw new TimeoutException("Failed to acquire file lock within timeout period");
            }

            // Execute the operation while holding the lock
            return await operation();
        }
        finally
        {
            // Release the lock by closing the file stream
            lockFileStream?.Dispose();
        }
    }

    /// <summary>
    /// Implement a lease-based locking mechanism for better fault tolerance
    /// </summary>
    public class LeaseBasedLockManager
    {
        private readonly string _leaseTableName = "DistributedLeases";
        private readonly TimeSpan _leaseRenewalInterval = TimeSpan.FromSeconds(10);
        private readonly TimeSpan _leaseDuration = TimeSpan.FromSeconds(30);

        public async Task<Lease> AcquireLeaseAsync(Session session, JET_DBID dbid, string resourceId, string ownerId)
        {
            var lease = new Lease
            {
                ResourceId = resourceId,
                OwnerId = ownerId,
                MachineName = Environment.MachineName,
                ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id,
                AcquiredAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(_leaseDuration)
            };

            // Start lease renewal task
            var cancellationTokenSource = new CancellationTokenSource();
            var renewalTask = Task.Run(async () =>
            {
                while (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(_leaseRenewalInterval, cancellationTokenSource.Token);
                    await RenewLeaseAsync(session, dbid, lease);
                }
            }, cancellationTokenSource.Token);

            lease.RenewalTask = renewalTask;
            lease.CancellationTokenSource = cancellationTokenSource;

            return lease;
        }

        private async Task RenewLeaseAsync(Session session, JET_DBID dbid, Lease lease)
        {
            using (var transaction = new Transaction(session))
            {
                JET_TABLEID tableid;
                Api.JetOpenTable(session, dbid, _leaseTableName, null, 0, OpenTableGrbit.None, out tableid);

                try
                {
                    // Find lease by resource ID and owner
                    Api.JetSetCurrentIndex(session, tableid, null);
                    Api.MakeKey(session, tableid, lease.ResourceId, System.Text.Encoding.UTF8, MakeKeyGrbit.NewKey);

                    if (Api.TrySeek(session, tableid, SeekGrbit.SeekEQ))
                    {
                        // Update expiration time
                        Api.JetPrepareUpdate(session, tableid, JET_prep.Replace);

                        var columnid = Api.GetTableColumnid(session, tableid, "ExpiresAt");
                        Api.SetColumn(session, tableid, columnid, DateTime.UtcNow.Add(_leaseDuration));

                        Api.JetUpdate(session, tableid, null, 0, out _);
                        transaction.Commit(CommitTransactionGrbit.None);
                    }
                }
                finally
                {
                    Api.JetCloseTable(session, tableid);
                }
            }
        }
    }

    public class Lease : IDisposable
    {
        public string ResourceId { get; set; }
        public string OwnerId { get; set; }
        public string MachineName { get; set; }
        public int ProcessId { get; set; }
        public DateTime AcquiredAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public Task RenewalTask { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; set; }

        public void Dispose()
        {
            CancellationTokenSource?.Cancel();
            try
            {
                RenewalTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch { }
            CancellationTokenSource?.Dispose();
        }
    }
}

/// <summary>
/// Optimistic concurrency control using version columns
/// </summary>
public class OptimisticConcurrencyHandler
{
    public async Task<bool> UpdateWithVersionCheckAsync(
        Session session,
        JET_TABLEID tableid,
        string keyColumn,
        string keyValue,
        long expectedVersion,
        Action<JET_TABLEID> updateAction)
    {
        using (var transaction = new Transaction(session))
        {
            // Seek to the record
            Api.JetSetCurrentIndex(session, tableid, null);
            Api.MakeKey(session, tableid, keyValue, System.Text.Encoding.UTF8, MakeKeyGrbit.NewKey);

            if (!Api.TrySeek(session, tableid, SeekGrbit.SeekEQ))
            {
                return false; // Record not found
            }

            // Check current version
            var versionColumnId = Api.GetTableColumnid(session, tableid, "Version");
            var currentVersion = Api.RetrieveColumnAsInt64(session, tableid, versionColumnId) ?? 0;

            if (currentVersion != expectedVersion)
            {
                // Version mismatch - someone else updated the record
                return false;
            }

            // Perform the update
            Api.JetPrepareUpdate(session, tableid, JET_prep.Replace);
            updateAction(tableid);
            Api.JetUpdate(session, tableid, null, 0, out _);

            transaction.Commit(CommitTransactionGrbit.None);
            return true;
        }
    }
}

/// <summary>
/// Distributed SAGA pattern implementation for long-running transactions
/// </summary>
public class DistributedSagaCoordinator
{
    private readonly EsentDistributedCoordinator _coordinator;

    public DistributedSagaCoordinator(EsentDistributedCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    public async Task<bool> ExecuteSagaAsync(string sagaId, List<SagaStep> steps)
    {
        var completedSteps = new Stack<SagaStep>();

        try
        {
            foreach (var step in steps)
            {
                // Acquire lock for the step's resource
                var lockId = await _coordinator.AcquireLockAsync(
                    step.ResourceId,
                    sagaId,
                    LockType.Exclusive,
                    TimeSpan.FromSeconds(30));

                try
                {
                    // Execute the step
                    await step.ExecuteAsync();
                    completedSteps.Push(step);
                }
                finally
                {
                    await _coordinator.ReleaseLockAsync(lockId);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            // Compensate in reverse order
            while (completedSteps.Count > 0)
            {
                var step = completedSteps.Pop();
                try
                {
                    await step.CompensateAsync();
                }
                catch (Exception compensateEx)
                {
                    // Log compensation failure
                    Console.WriteLine($"Compensation failed for step {step.Name}: {compensateEx.Message}");
                }
            }

            throw;
        }
    }
}

public class SagaStep
{
    public string Name { get; set; }
    public string ResourceId { get; set; }
    public Func<Task> ExecuteAsync { get; set; }
    public Func<Task> CompensateAsync { get; set; }
}

// Example usage
public class DistributedWorkflowExample
{
    public async Task ProcessDistributedWorkflow()
    {
        var dbPath = @"\\shared\database\workflow.edb";

        using (var handler = new SharedVolumeEsentHandler(dbPath))
        {
            await handler.ExecuteWithFileLockAsync(async () =>
            {
                using (var coordinator = new EsentDistributedCoordinator("WorkflowInstance", dbPath))
                {
                    var sagaCoordinator = new DistributedSagaCoordinator(coordinator);

                    var steps = new List<SagaStep>
                    {
                        new SagaStep
                        {
                            Name = "DebitAccount",
                            ResourceId = "account-123",
                            ExecuteAsync = async () => { /* Debit logic */ },
                            CompensateAsync = async () => { /* Refund logic */ }
                        },
                        new SagaStep
                        {
                            Name = "CreditAccount",
                            ResourceId = "account-456",
                            ExecuteAsync = async () => { /* Credit logic */ },
                            CompensateAsync = async () => { /* Reverse credit */ }
                        }
                    };

                    await sagaCoordinator.ExecuteSagaAsync(Guid.NewGuid().ToString(), steps);
                }

                return true;
            });
        }
    }
}

```

### DB File on Shared Volume

#### Key Considerations

1. File-Based Locking for Shared Volume

Use a lock file with exclusive access to coordinate database access
Include process information for debugging
Use FileOptions.DeleteOnClose for automatic cleanup

2. Lease-Based Locking

Implement lease renewal to handle process crashes
Automatic expiration prevents deadlocks
Background task maintains lease while active

3. Version-Based Optimistic Concurrency

Check version before updates
Retry with exponential backoff on conflicts
Track version numbers throughout transaction

4. SAGA Pattern for Long Transactions

Break down complex operations into steps
Each step has a compensating action
Automatic rollback on failure

#### Best Practices

- Network Resilience:
  - Handle network interruptions gracefully,
  - Implement retry logic with exponential backoff
  - Use timeouts for all operations
- Clock Synchronization
  - Ensure all machines have synchronized clocks (use NTP)
  - Use UTC timestamps everywhere
  - Consider using logical clocks for ordering
- Monitoring and Diagnostics
  - Log all lock acquisitions and releases
  - Track version conflicts and retry counts
  - Monitor lease expiration and renewal
- Performance Optimization
  - Minimize lock hold time
  - Use shared locks when possible
  - Batch operations to reduce round trips
- Failure Handling
  - Implement dead process detection
  - Clean up orphaned locks
  - Use compensating transactions for recovery

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Isam.Esent.Interop;

/// <summary>
/// Handles ESENT database access on shared volumes with proper synchronization
/// </summary>
public class SharedVolumeEsentHandler
{
    private readonly string _databasePath;
    private readonly string _lockFilePath;
    private readonly TimeSpan _lockTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _lockRetryInterval = TimeSpan.FromMilliseconds(100);

    public SharedVolumeEsentHandler(string databasePath)
    {
        _databasePath = databasePath;
        _lockFilePath = Path.Combine(Path.GetDirectoryName(databasePath), $"{Path.GetFileName(databasePath)}.lock");
    }

    /// <summary>
    /// Execute an operation with file-based locking for shared volume access
    /// </summary>
    public async Task<T> ExecuteWithFileLockAsync<T>(Func<Task<T>> operation)
    {
        var lockAcquired = false;
        var startTime = DateTime.UtcNow;
        FileStream lockFileStream = null;

        try
        {
            while (!lockAcquired && DateTime.UtcNow - startTime < _lockTimeout)
            {
                try
                {
                    // Try to create/open lock file exclusively
                    lockFileStream = new FileStream(
                        _lockFilePath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        4096,
                        FileOptions.DeleteOnClose);

                    // Write process information to lock file
                    var lockInfo = $"{Environment.MachineName}|{System.Diagnostics.Process.GetCurrentProcess().Id}|{DateTime.UtcNow}";
                    var bytes = System.Text.Encoding.UTF8.GetBytes(lockInfo);
                    await lockFileStream.WriteAsync(bytes, 0, bytes.Length);
                    await lockFileStream.FlushAsync();

                    lockAcquired = true;
                }
                catch (IOException)
                {
                    // Lock file is in use by another process
                    await Task.Delay(_lockRetryInterval);
                }
            }

            if (!lockAcquired)
            {
                throw new TimeoutException("Failed to acquire file lock within timeout period");
            }

            // Execute the operation while holding the lock
            return await operation();
        }
        finally
        {
            // Release the lock by closing the file stream
            lockFileStream?.Dispose();
        }
    }

    /// <summary>
    /// Implement a lease-based locking mechanism for better fault tolerance
    /// </summary>
    public class LeaseBasedLockManager
    {
        private readonly string _leaseTableName = "DistributedLeases";
        private readonly TimeSpan _leaseRenewalInterval = TimeSpan.FromSeconds(10);
        private readonly TimeSpan _leaseDuration = TimeSpan.FromSeconds(30);

        public async Task<Lease> AcquireLeaseAsync(Session session, JET_DBID dbid, string resourceId, string ownerId)
        {
            var lease = new Lease
            {
                ResourceId = resourceId,
                OwnerId = ownerId,
                MachineName = Environment.MachineName,
                ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id,
                AcquiredAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(_leaseDuration)
            };

            // Start lease renewal task
            var cancellationTokenSource = new CancellationTokenSource();
            var renewalTask = Task.Run(async () =>
            {
                while (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(_leaseRenewalInterval, cancellationTokenSource.Token);
                    await RenewLeaseAsync(session, dbid, lease);
                }
            }, cancellationTokenSource.Token);

            lease.RenewalTask = renewalTask;
            lease.CancellationTokenSource = cancellationTokenSource;

            return lease;
        }

        private async Task RenewLeaseAsync(Session session, JET_DBID dbid, Lease lease)
        {
            using (var transaction = new Transaction(session))
            {
                JET_TABLEID tableid;
                Api.JetOpenTable(session, dbid, _leaseTableName, null, 0, OpenTableGrbit.None, out tableid);

                try
                {
                    // Find lease by resource ID and owner
                    Api.JetSetCurrentIndex(session, tableid, null);
                    Api.MakeKey(session, tableid, lease.ResourceId, System.Text.Encoding.UTF8, MakeKeyGrbit.NewKey);

                    if (Api.TrySeek(session, tableid, SeekGrbit.SeekEQ))
                    {
                        // Update expiration time
                        Api.JetPrepareUpdate(session, tableid, JET_prep.Replace);

                        var columnid = Api.GetTableColumnid(session, tableid, "ExpiresAt");
                        Api.SetColumn(session, tableid, columnid, DateTime.UtcNow.Add(_leaseDuration));

                        Api.JetUpdate(session, tableid, null, 0, out _);
                        transaction.Commit(CommitTransactionGrbit.None);
                    }
                }
                finally
                {
                    Api.JetCloseTable(session, tableid);
                }
            }
        }
    }

    public class Lease : IDisposable
    {
        public string ResourceId { get; set; }
        public string OwnerId { get; set; }
        public string MachineName { get; set; }
        public int ProcessId { get; set; }
        public DateTime AcquiredAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public Task RenewalTask { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; set; }

        public void Dispose()
        {
            CancellationTokenSource?.Cancel();
            try
            {
                RenewalTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch { }
            CancellationTokenSource?.Dispose();
        }
    }
}

/// <summary>
/// Optimistic concurrency control using version columns
/// </summary>
public class OptimisticConcurrencyHandler
{
    public async Task<bool> UpdateWithVersionCheckAsync(
        Session session,
        JET_TABLEID tableid,
        string keyColumn,
        string keyValue,
        long expectedVersion,
        Action<JET_TABLEID> updateAction)
    {
        using (var transaction = new Transaction(session))
        {
            // Seek to the record
            Api.JetSetCurrentIndex(session, tableid, null);
            Api.MakeKey(session, tableid, keyValue, System.Text.Encoding.UTF8, MakeKeyGrbit.NewKey);

            if (!Api.TrySeek(session, tableid, SeekGrbit.SeekEQ))
            {
                return false; // Record not found
            }

            // Check current version
            var versionColumnId = Api.GetTableColumnid(session, tableid, "Version");
            var currentVersion = Api.RetrieveColumnAsInt64(session, tableid, versionColumnId) ?? 0;

            if (currentVersion != expectedVersion)
            {
                // Version mismatch - someone else updated the record
                return false;
            }

            // Perform the update
            Api.JetPrepareUpdate(session, tableid, JET_prep.Replace);
            updateAction(tableid);
            Api.JetUpdate(session, tableid, null, 0, out _);

            transaction.Commit(CommitTransactionGrbit.None);
            return true;
        }
    }
}

/// <summary>
/// Distributed SAGA pattern implementation for long-running transactions
/// </summary>
public class DistributedSagaCoordinator
{
    private readonly EsentDistributedCoordinator _coordinator;

    public DistributedSagaCoordinator(EsentDistributedCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    public async Task<bool> ExecuteSagaAsync(string sagaId, List<SagaStep> steps)
    {
        var completedSteps = new Stack<SagaStep>();

        try
        {
            foreach (var step in steps)
            {
                // Acquire lock for the step's resource
                var lockId = await _coordinator.AcquireLockAsync(
                    step.ResourceId,
                    sagaId,
                    LockType.Exclusive,
                    TimeSpan.FromSeconds(30));

                try
                {
                    // Execute the step
                    await step.ExecuteAsync();
                    completedSteps.Push(step);
                }
                finally
                {
                    await _coordinator.ReleaseLockAsync(lockId);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            // Compensate in reverse order
            while (completedSteps.Count > 0)
            {
                var step = completedSteps.Pop();
                try
                {
                    await step.CompensateAsync();
                }
                catch (Exception compensateEx)
                {
                    // Log compensation failure
                    Console.WriteLine($"Compensation failed for step {step.Name}: {compensateEx.Message}");
                }
            }

            throw;
        }
    }
}

public class SagaStep
{
    public string Name { get; set; }
    public string ResourceId { get; set; }
    public Func<Task> ExecuteAsync { get; set; }
    public Func<Task> CompensateAsync { get; set; }
}

// Example usage
public class DistributedWorkflowExample
{
    public async Task ProcessDistributedWorkflow()
    {
        var dbPath = @"\\shared\database\workflow.edb";

        using (var handler = new SharedVolumeEsentHandler(dbPath))
        {
            await handler.ExecuteWithFileLockAsync(async () =>
            {
                using (var coordinator = new EsentDistributedCoordinator("WorkflowInstance", dbPath))
                {
                    var sagaCoordinator = new DistributedSagaCoordinator(coordinator);

                    var steps = new List<SagaStep>
                    {
                        new SagaStep
                        {
                            Name = "DebitAccount",
                            ResourceId = "account-123",
                            ExecuteAsync = async () => { /* Debit logic */ },
                            CompensateAsync = async () => { /* Refund logic */ }
                        },
                        new SagaStep
                        {
                            Name = "CreditAccount",
                            ResourceId = "account-456",
                            ExecuteAsync = async () => { /* Credit logic */ },
                            CompensateAsync = async () => { /* Reverse credit */ }
                        }
                    };

                    await sagaCoordinator.ExecuteSagaAsync(Guid.NewGuid().ToString(), steps);
                }

                return true;
            });
        }
    }
}
```

## Comparison with Other Providers

| Feature | ESENT | FileSystem | InMemory | ClusterRegistry |
|---------|-------|------------|----------|-----------------|
| Performance | High | Medium | Very High | Medium |
| Persistence | Yes | Yes | No | Yes |
| Transactions | Full ACID | Basic | Full | Batch only |
| Platform | Windows | Any | Any | Windows Cluster |
| Max Size | 16TB | OS Limit | RAM | Registry Limit |
| Concurrent Users | High | Medium | High | Medium |
| Query Support | Indexed | None | None | None |
| Backup | Online | File Copy | Snapshot | Registry Export |

## Related Documentation

- [Common.Persistence Overview](../Common.Persistence/README.md)
- [Transaction Management](../Common.Tx/README.md)
- [Provider Comparison Guide](../docs/provider-comparison.md)
- [Microsoft ESENT Documentation](https://docs.microsoft.com/en-us/windows/win32/extensible-storage-engine/)