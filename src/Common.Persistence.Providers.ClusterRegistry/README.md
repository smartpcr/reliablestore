# ClusterRegistry Persistence Provider

## Overview

The ClusterRegistry persistence provider enables highly available, distributed storage using the Windows Failover Cluster Registry. This provider is designed for mission-critical applications requiring zero-downtime deployments and automatic failover capabilities.

## Features

- **High Availability**: Automatic failover with Windows Failover Clustering
- **Distributed Storage**: Data replicated across all cluster nodes
- **Zero Downtime**: Rolling updates and maintenance without service interruption
- **ACID Transactions**: Full transactional support with cluster-wide consistency
- **Automatic Failover**: Seamless recovery from node failures
- **Quorum-based Consensus**: Prevents split-brain scenarios
- **Resource Monitoring**: Built-in health checks and automatic recovery
- **Multi-node Support**: Scales from 2 to 64 nodes per cluster
- **Data Locality**: Optional node affinity for performance optimization
- **Versioning Support**: Built-in entity versioning and conflict resolution

## Prerequisites

### System Requirements
- Windows Server 2016 or later (2019/2022 recommended)
- Windows Failover Clustering feature installed
- .NET 9.0 or later
- Administrator privileges for cluster operations

### Cluster Requirements
- Minimum 2 nodes (3+ recommended for better quorum)
- Shared storage (SAN, Storage Spaces Direct, or CSV)
- Dedicated cluster network recommended
- Active Directory domain (optional but recommended)

## Configuration

### Basic Setup

```csharp
var config = new ClusterPersistenceConfiguration
{
    ClusterName = "PROD-CLUSTER",
    ResourceGroupName = "ReliableStore-RG",
    RegistryKeyPath = @"Software\ReliableStore\Data",
    MaxValueSizeKB = 64,
    EnableCompression = true,
    ReplicationMode = ReplicationMode.Synchronous,
    FailoverTimeout = TimeSpan.FromSeconds(30)
};

var store = new ClusterPersistenceStore<Product>(config);
await store.InitializeAsync();
```

### Configuration Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ClusterName` | string | Local cluster | Target cluster name |
| `ResourceGroupName` | string | Required | Cluster resource group |
| `RegistryKeyPath` | string | Required | Registry path for data |
| `MaxValueSizeKB` | int | `64` | Maximum size per value |
| `EnableCompression` | bool | `true` | Enable data compression |
| `ReplicationMode` | enum | `Synchronous` | Sync or async replication |
| `FailoverTimeout` | TimeSpan | `30s` | Max failover duration |
| `NodeAffinity` | string[] | All nodes | Preferred nodes list |
| `EnableEncryption` | bool | `false` | Encrypt data at rest |
| `ConflictResolution` | enum | `LastWrite` | Conflict resolution strategy |
| `HealthCheckInterval` | TimeSpan | `10s` | Health monitoring interval |
| `MaxRetries` | int | `3` | Operation retry count |
| `RetryDelay` | TimeSpan | `1s` | Delay between retries |

## Usage Examples

### Basic Operations

```csharp
// Initialize store
var store = new ClusterPersistenceStore<Customer>(config);
await store.InitializeAsync();

// Save entity
var customer = new Customer 
{ 
    Id = "CUST-001", 
    Name = "Acme Corp",
    Tier = CustomerTier.Premium
};
await store.SaveAsync(customer.Id, customer);

// Read entity
var retrieved = await store.GetAsync("CUST-001");

// Update with optimistic concurrency
retrieved.Tier = CustomerTier.Enterprise;
await store.SaveAsync(retrieved.Id, retrieved, retrieved.Version);

// Delete
await store.DeleteAsync("CUST-001");

// Query all
var allCustomers = await store.GetAllAsync();
```

### Transactional Operations

```csharp
using var transaction = transactionFactory.CreateTransaction();
var operation = new ClusterPersistenceOperation(store);
transaction.EnlistResource(operation);

try
{
    await operation.SaveAsync("cust1", customer1);
    await operation.SaveAsync("cust2", customer2);
    await operation.SaveAsync("order1", order);
    
    await transaction.CommitAsync();
}
catch (ClusterPersistenceException ex)
{
    logger.LogError(ex, "Transaction failed, will rollback");
    await transaction.RollbackAsync();
    throw;
}
```

### High Availability Patterns

```csharp
// Configure for maximum availability
var haConfig = new ClusterPersistenceConfiguration
{
    ClusterName = "PROD-CLUSTER",
    ResourceGroupName = "CriticalData-RG",
    RegistryKeyPath = @"Software\Mission\Critical",
    
    // HA-optimized settings
    ReplicationMode = ReplicationMode.Synchronous,
    FailoverTimeout = TimeSpan.FromSeconds(15),
    HealthCheckInterval = TimeSpan.FromSeconds(5),
    MaxRetries = 5,
    RetryDelay = TimeSpan.FromMilliseconds(500),
    
    // Node distribution
    NodeAffinity = new[] { "NODE1", "NODE2", "NODE3" },
    MinimumNodes = 2,
    
    // Data protection
    EnableEncryption = true,
    EnableCompression = true,
    BackupEnabled = true,
    BackupInterval = TimeSpan.FromHours(1)
};
```

## Architecture

### Storage Layout

The provider organizes data in the cluster registry as follows:

```
HKEY_LOCAL_MACHINE\Software\ReliableStore\Data\
├── Entities\
│   ├── Customer\
│   │   ├── CUST-001 (REG_BINARY: compressed JSON)
│   │   ├── CUST-002
│   │   └── _metadata (REG_BINARY: index data)
│   ├── Order\
│   │   ├── ORD-001
│   │   └── ORD-002
│   └── Product\
│       ├── PROD-001
│       └── PROD-002
├── Transactions\
│   ├── TX-12345 (REG_BINARY: transaction log)
│   └── TX-12346
└── Configuration\
    ├── Version (REG_DWORD)
    └── Schema (REG_SZ)
```

### Replication Strategy

1. **Synchronous Replication** (Default)
   - Write completes only after all nodes acknowledge
   - Zero data loss guarantee
   - Higher latency for write operations

2. **Asynchronous Replication**
   - Write completes after primary node persists
   - Better performance, minimal data loss risk
   - Suitable for geo-distributed clusters

### Failover Behavior

1. **Automatic Failover**
   - Triggered by node failure or resource failure
   - Typically completes in 10-30 seconds
   - No data loss with synchronous replication

2. **Manual Failover**
   - Administrator-initiated for maintenance
   - Graceful transition with zero downtime
   - Pre-failover validation ensures data consistency

## Performance Characteristics

### Strengths
- **Read Performance**: Excellent with local node caching
- **Write Performance**: Good with async replication
- **Scalability**: Linear read scaling with more nodes
- **Availability**: 99.99% uptime achievable
- **Consistency**: Strong consistency guarantees

### Performance Metrics
| Operation | Typical Performance | Notes |
|-----------|-------------------|-------|
| Single Read (cached) | < 0.1ms | From local node cache |
| Single Read (cluster) | 1-5ms | From registry |
| Single Write (sync) | 10-20ms | All nodes acknowledge |
| Single Write (async) | 2-5ms | Primary only |
| Batch Write (100 items) | 100-200ms | Optimized batching |
| Failover Time | 15-30s | Depends on cluster config |

### Limitations
- **Value Size**: Registry values limited to 1MB (configurable)
- **Key Length**: Maximum 255 characters for key names
- **Total Size**: Registry hive size limitations apply
- **Platform**: Windows Server with Failover Clustering only
- **Network Latency**: Performance sensitive to cluster network

## Best Practices

### 1. Cluster Design
- Use odd number of nodes (3, 5) for better quorum
- Dedicated cluster network for replication traffic
- Storage Spaces Direct for shared storage
- Separate management and cluster networks

### 2. Data Organization
- Use hierarchical key structure for organization
- Keep individual values under 64KB
- Batch related updates in transactions
- Implement data archival for old data

### 3. Performance Optimization
```csharp
// Enable caching for read-heavy workloads
var config = new ClusterPersistenceConfiguration
{
    EnableLocalCache = true,
    CacheSize = 1000,
    CacheTTL = TimeSpan.FromMinutes(5),
    PreloadCache = true
};

// Use async operations for better throughput
var tasks = items.Select(item => 
    store.SaveAsync(item.Id, item)
);
await Task.WhenAll(tasks);
```

### 4. Monitoring and Alerting
```csharp
// Subscribe to cluster events
store.NodeFailover += (sender, args) =>
{
    logger.LogWarning($"Failover from {args.OldNode} to {args.NewNode}");
    alertingService.SendFailoverAlert(args);
};

store.HealthCheckFailed += (sender, args) =>
{
    logger.LogError($"Health check failed: {args.Reason}");
    alertingService.SendHealthAlert(args);
};

// Monitor performance metrics
var metrics = await store.GetPerformanceMetricsAsync();
if (metrics.AverageWriteLatency > TimeSpan.FromMilliseconds(50))
{
    logger.LogWarning("Write latency exceeding threshold");
}
```

## Security Considerations

### Access Control
- Cluster service account requires registry permissions
- Use dedicated service accounts with minimal privileges
- Enable Windows Firewall rules for cluster traffic
- Implement role-based access control (RBAC)

### Data Protection
```csharp
var secureConfig = new ClusterPersistenceConfiguration
{
    // Encryption at rest
    EnableEncryption = true,
    EncryptionAlgorithm = "AES256",
    KeyManagementService = "AzureKeyVault",
    
    // Audit logging
    EnableAuditLogging = true,
    AuditLogPath = @"\\secure-share\audit-logs",
    
    // Network security
    RequireKerberos = true,
    EnableIPSec = true
};
```

## Troubleshooting

### Common Issues

1. **Initialization Failures**
   - Verify cluster service is running
   - Check cluster network connectivity
   - Ensure proper permissions on registry keys
   - Review Windows Event Log for cluster errors

2. **Performance Issues**
   - Monitor cluster network utilization
   - Check for registry fragmentation
   - Verify node resource allocation
   - Review replication queue depth

3. **Failover Problems**
   - Validate quorum configuration
   - Check witness resource availability
   - Ensure all nodes can access shared storage
   - Review cluster validation report

### Diagnostic Commands

```powershell
# Check cluster health
Test-Cluster -Node NODE1,NODE2,NODE3

# View resource group status
Get-ClusterGroup "ReliableStore-RG" | Get-ClusterResource

# Monitor replication status
Get-ClusterLog -TimeSpan 10 -Destination C:\Logs\

# Registry key permissions
Get-Acl "HKLM:\Software\ReliableStore\Data" | Format-List
```

## Migration Guide

### From Other Providers

```csharp
public async Task MigrateFromFileSystemAsync(
    FileStore<T> source, 
    ClusterPersistenceStore<T> target)
{
    var items = await source.GetAllAsync();
    
    // Batch migration with progress tracking
    var batches = items.Chunk(100);
    var completed = 0;
    
    foreach (var batch in batches)
    {
        var operations = batch.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value
        );
        
        await target.SaveBatchAsync(operations);
        completed += batch.Count();
        
        logger.LogInformation($"Migrated {completed}/{items.Count} items");
    }
}
```

### Cluster Upgrade Strategy

1. **Rolling Upgrade** (Zero Downtime)
   ```powershell
   # Drain node
   Suspend-ClusterNode -Name NODE1 -Drain
   
   # Perform upgrade
   # ... upgrade operations ...
   
   # Resume node
   Resume-ClusterNode -Name NODE1
   ```

2. **Blue-Green Deployment**
   - Create new cluster with updated configuration
   - Replicate data to new cluster
   - Switch application traffic
   - Decommission old cluster

## Advanced Scenarios

### Geo-Distributed Clusters

```csharp
var geoConfig = new ClusterPersistenceConfiguration
{
    ClusterName = "GLOBAL-CLUSTER",
    
    // Multi-site configuration
    Sites = new[]
    {
        new ClusterSite { Name = "US-EAST", Nodes = new[] { "USE1", "USE2" } },
        new ClusterSite { Name = "US-WEST", Nodes = new[] { "USW1", "USW2" } },
        new ClusterSite { Name = "EU-WEST", Nodes = new[] { "EUW1", "EUW2" } }
    },
    
    // Async replication between sites
    ReplicationMode = ReplicationMode.Asynchronous,
    SiteReplicationDelay = TimeSpan.FromSeconds(1),
    
    // Preferred site for reads
    PreferredSite = "US-EAST",
    
    // Conflict resolution
    ConflictResolution = ConflictResolution.LastWriteWins,
    ConflictResolutionWindow = TimeSpan.FromMinutes(5)
};
```

### Integration with Service Fabric

```csharp
public class ServiceFabricClusterProvider<T> : ClusterPersistenceStore<T>
    where T : class
{
    private readonly StatefulServiceContext _context;
    
    protected override string GetNodeName()
    {
        return _context.NodeContext.NodeName;
    }
    
    protected override async Task<bool> IsHealthyAsync()
    {
        var health = await _context.CodePackageActivationContext
            .GetHealthAsync();
        return health.AggregatedHealthState == HealthState.Ok;
    }
}
```

## Related Documentation

- [Common.Persistence Overview](../Common.Persistence/README.md)
- [Windows Failover Clustering Guide](https://docs.microsoft.com/en-us/windows-server/failover-clustering/)
- [Transaction Management](../Common.Tx/README.md)
- [High Availability Best Practices](../docs/high-availability.md)