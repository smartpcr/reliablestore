# ReliableStore Persistence Providers Guide

## Overview

ReliableStore offers a flexible, provider-based persistence architecture that allows you to choose the optimal storage backend for your specific requirements. All providers implement the same interfaces, making it easy to switch between providers or use different providers for different entity types.

## Available Providers

### 1. FileSystem Provider
**Best for**: Development, small-to-medium deployments, human-readable storage

- **Storage**: JSON files on local file system
- **Platform**: Cross-platform (Windows, Linux, macOS)
- **Performance**: Consistent 2-3ms per operation, scales linearly
- **Scalability**: Limited by file system (millions of files possible)
- **Use Cases**: Development, testing, production with moderate load, configuration storage

[Full Documentation →](../src/Common.Persistence.Providers.FileSystem/README.md)

### 2. InMemory Provider  
**Best for**: Testing, caching, temporary data

- **Storage**: RAM (volatile)
- **Platform**: Cross-platform
- **Performance**: Ultra-fast (0.002-27ms for any size)
- **Scalability**: Limited by available memory
- **Use Cases**: Unit tests, integration tests, caching layer, session storage

[Full Documentation →](../src/Common.Persistence.Providers.InMemory/README.md)

### 3. ESENT Provider
**Best for**: High-performance Windows applications, local databases

- **Storage**: Embedded database (ESENT)
- **Platform**: Windows only
- **Performance**: High (native integration)
- **Scalability**: Up to 16TB per database
- **Use Cases**: Desktop applications, Windows services, local data stores

[Full Documentation →](../src/Common.Persistence.Providers.Esent/README.md)

### 4. ClusterRegistry Provider
**Best for**: High availability requirements with tiny data payloads

- **Storage**: Windows Failover Cluster Registry
- **Platform**: Windows Server with Failover Clustering
- **Performance**: Fast for tiny values (<1KB), degrades exponentially with size
- **Scalability**: 2-64 nodes, severely limited by payload size
- **Use Cases**: Small configuration data, feature flags, service discovery

[Full Documentation →](../src/Common.Persistence.Providers.ClusterRegistry/README.md)

## Provider Comparison Matrix

| Feature | FileSystem | InMemory | ESENT | ClusterRegistry |
|---------|------------|----------|-------|-----------------|
| **Persistence** | ✅ Yes | ❌ No | ✅ Yes | ✅ Yes |
| **Platform** | 🌍 Any | 🌍 Any | 🪟 Windows | 🪟 Windows Server |
| **Performance** | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐† |
| **Transactions** | ✅ Basic | ✅ Full | ✅ Full ACID | ✅ Batch only |
| **Concurrency** | 🔒 File locks | 🔒 Thread-safe | 🔒 Row-level | 🔒 Distributed |
| **Max Size** | 💾 OS limit | 💾 RAM | 💾 16TB | 💾 Memory limit* |
| **Query Support** | ❌ None | ❌ None | ✅ Indexed | ❌ None |
| **High Availability** | ❌ No | ❌ No | ❌ No | ✅ Automatic |
| **Backup** | 📁 File copy | 📸 Snapshot | 💾 Online | 📋 Export |
| **Setup Complexity** | ⭐ Simple | ⭐ None | ⭐⭐ Moderate | ⭐⭐⭐⭐ Complex |

† Good for tiny payloads (<1KB) but degrades exponentially with size
* Registry values can use available memory but Microsoft recommends <2KB for performance

### Performance Comparison

Based on recent benchmarks (Windows 10/11, Intel i9-13900K, .NET Framework 4.7.2 & .NET 9.0):

| Operation | FileSystem | InMemory | ESENT | ClusterRegistry |
|-----------|------------|----------|-------|-----------------|
| **Small (16B)** | 2.5ms | **0.002ms** ✅ | 0.4-0.5ms | 0.37ms |
| **Medium (16KB)** | 2.3ms | **0.07ms** ✅ | 0.5ms | 30ms ⚠️ |
| **Large (5MB)** | 64ms | **27ms** ✅ | 2-2.3s | 6,731ms ❌ |
| **Memory Usage (5MB)** | 10MB | **5MB** ✅ | 1.5GB | 25GB ❌ |
| **Scaling** | Linear | **Excellent** | Good | Poor |
| **10K Ops (100KB)** | ✅ Supported | ✅ Supported | ✅ 20-28s | ❌ Not Supported |

Key findings:
- **InMemory**: Fastest for all payload sizes (2-1000x faster)
- **FileSystem**: Consistent 2-3ms overhead, scales well
- **ClusterRegistry**: Fast for tiny payloads (<1KB) but degrades exponentially
- **ESENT**: Moderate performance, good for indexed queries

⚠️ **Critical**: ClusterRegistry performance degrades severely with payload size (>16KB)

## Choosing the Right Provider

### Decision Tree

```
Start → Is data temporary?
         ├─ Yes → InMemory Provider
         └─ No → Continue
                  ↓
         Need high availability?
         ├─ Yes → Data size per item?
         │        ├─ <1KB items → Windows Server? → Yes → ClusterRegistry
         │        │                              └─ No → External solutions
         │        └─ >1KB items → Consider FileSystem/ESENT + replication
         └─ No → Continue
                  ↓
         Windows-only deployment?
         ├─ Yes → Need high performance?
         │        ├─ Yes → ESENT Provider
         │        └─ No → FileSystem Provider
         └─ No → FileSystem Provider
```

### Important Performance Considerations

⚠️ **ClusterRegistry Limitations**:
- Performance degrades exponentially with payload size (80x slower at 16KB, 100x slower at 5MB)
- Massive memory overhead for large payloads (25GB for 5MB payload)
- Cannot handle large datasets due to registry constraints
- Only suitable for tiny payloads (<1KB) where it performs well
- Not recommended for any data over 16KB

✅ **ClusterRegistry Strengths**:
- Fast for very small payloads (<1KB) 
- Automatic high availability with Windows Failover Clustering
- Good for small configuration values and feature flags
- Distributed locking for cluster-wide consistency

### Use Case Recommendations

#### Development & Testing
```csharp
// Use InMemory for unit tests
services.AddSingleton<IStore<Product>>(
    new InMemoryStore<Product>());

// Use FileSystem for integration tests
services.AddSingleton<IStore<Product>>(
    new FileStore<Product>("products", new FileSystemStoreSettings
    {
        BasePath = "test-data",
        UsePrettyJson = true
    }));
```

#### Single-Server Production
```csharp
// Windows: Use ESENT for performance
services.AddSingleton<IStore<Product>>(
    new SimpleEsentProvider<Product>(new EsentStoreSettings
    {
        DatabasePath = "data/products.edb",
        CacheSizeMB = 256
    }));

// Cross-platform: Use FileSystem
services.AddSingleton<IStore<Product>>(
    new FileStore<Product>("products", new FileSystemStoreSettings
    {
        BasePath = "/var/data/reliablestore"
    }));
```

#### High Availability Production
```csharp
// For small metadata/config (Windows clusters)
services.AddSingleton<IStore<ServiceConfig>>(
    new ClusterPersistenceStore<ServiceConfig>(new ClusterPersistenceConfiguration
    {
        ClusterName = "PROD-CLUSTER",
        ResourceGroupName = "App-RG",
        RegistryKeyPath = @"Software\App\Config",
        MaxValueSize = 64 * 1024  // 64KB limit for performance
    }));

// For larger data with HA requirements
// Option 1: ESENT + Windows Failover Clustering with shared storage
services.AddSingleton<IStore<Product>>(
    new SimpleEsentProvider<Product>(new EsentStoreSettings
    {
        DatabasePath = @"S:\SharedStorage\products.edb",  // Cluster shared volume
        CacheSizeMB = 512
    }));

// Option 2: FileSystem + External replication (e.g., DFS-R, rsync)
services.AddSingleton<IStore<Product>>(
    new FileStore<Product>("products", new FileSystemStoreSettings
    {
        BasePath = @"D:\ReplicatedData"  // Replicated via DFS-R
    }));
```

## Common Implementation Patterns

### 1. Provider Factory Pattern
```csharp
public interface IStoreFactory
{
    IStore<T> CreateStore<T>(string storeName) where T : class;
}

public class StoreFactory : IStoreFactory
{
    private readonly IConfiguration _configuration;
    
    public IStore<T> CreateStore<T>(string storeName) where T : class
    {
        var providerType = _configuration[$"Storage:{storeName}:Provider"];
        
        return providerType switch
        {
            "FileSystem" => CreateFileSystemStore<T>(storeName),
            "InMemory" => new InMemoryStore<T>(),
            "ESENT" => CreateEsentStore<T>(storeName),
            "ClusterRegistry" => CreateClusterStore<T>(storeName),
            _ => throw new NotSupportedException($"Unknown provider: {providerType}")
        };
    }
}
```

### 2. Multi-Provider Strategy
```csharp
// Use different providers for different data types
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Volatile session data in memory
        services.AddSingleton<IStore<Session>>(
            new InMemoryStore<Session>());
        
        // Product catalog in ESENT for fast queries
        services.AddSingleton<IStore<Product>>(
            new SimpleEsentProvider<Product>(esentSettings));
        
        // Orders in ESENT for scalability (ClusterRegistry limited to <10K items)
        services.AddSingleton<IStore<Order>>(
            new SimpleEsentProvider<Order>(esentSettings));
        
        // Small configuration in cluster registry for HA
        services.AddSingleton<IStore<ServiceConfig>>(
            new ClusterPersistenceStore<ServiceConfig>(clusterConfig));
            
        // Audit logs in file system for compliance  
        services.AddSingleton<IStore<AuditLog>>(
            new FileStore<AuditLog>("audit", fileSettings));
    }
}
```

### 3. Caching Layer Pattern
```csharp
public class CachedStore<T> : IStore<T> where T : class
{
    private readonly IStore<T> _persistentStore;
    private readonly IStore<T> _cacheStore;
    private readonly TimeSpan _cacheDuration;
    
    public CachedStore(IStore<T> persistentStore, TimeSpan cacheDuration)
    {
        _persistentStore = persistentStore;
        _cacheStore = new InMemoryStore<T>();
        _cacheDuration = cacheDuration;
    }
    
    public async Task<T> GetAsync(string key)
    {
        // Try cache first
        var cached = await _cacheStore.GetAsync(key);
        if (cached != null) return cached;
        
        // Load from persistent store
        var item = await _persistentStore.GetAsync(key);
        if (item != null)
        {
            await _cacheStore.SaveAsync(key, item);
        }
        
        return item;
    }
}
```

## Migration Between Providers

### Generic Migration Utility
```csharp
public class StoreMigrator<T> where T : class
{
    public async Task MigrateAsync(
        IStore<T> source, 
        IStore<T> target,
        IProgress<int> progress = null,
        CancellationToken cancellationToken = default)
    {
        var items = await source.GetAllAsync();
        var total = items.Count;
        var completed = 0;
        
        foreach (var batch in items.Chunk(100))
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var tasks = batch.Select(kvp => 
                target.SaveAsync(kvp.Key, kvp.Value)
            );
            
            await Task.WhenAll(tasks);
            
            completed += batch.Count();
            progress?.Report(completed * 100 / total);
        }
    }
}

// Usage
var migrator = new StoreMigrator<Product>();
await migrator.MigrateAsync(
    source: fileStore,
    target: esentStore,
    progress: new Progress<int>(percent => 
        Console.WriteLine($"Migration progress: {percent}%"))
);
```

## Performance Optimization Tips

### 1. Batching Operations
All providers benefit from batching:
```csharp
// Instead of multiple individual saves
foreach (var product in products)
{
    await store.SaveAsync(product.Id, product); // Slow
}

// Use batch operations
var batch = products.ToDictionary(p => p.Id, p => p);
await store.SaveBatchAsync(batch); // Fast
```

### 2. Connection Pooling
For providers that support it:
```csharp
// ESENT with connection pooling
var settings = new EsentStoreSettings
{
    MaxSessions = 100,  // Pool size
    SessionTimeout = TimeSpan.FromMinutes(5)
};
```

### 3. Async Patterns
Always use async methods:
```csharp
// Good - non-blocking
var tasks = ids.Select(id => store.GetAsync(id));
var results = await Task.WhenAll(tasks);

// Bad - blocks threads
var results = ids.Select(id => 
    store.GetAsync(id).Result  // Don't do this!
).ToList();
```

## Monitoring and Diagnostics

### Provider Metrics
```csharp
public interface IStoreMetrics
{
    long ReadCount { get; }
    long WriteCount { get; }
    long ErrorCount { get; }
    TimeSpan AverageReadTime { get; }
    TimeSpan AverageWriteTime { get; }
}

public class MonitoredStore<T> : IStore<T> where T : class
{
    private readonly IStore<T> _innerStore;
    private readonly IMetricsCollector _metrics;
    
    public async Task<T> GetAsync(string key)
    {
        using var timer = _metrics.StartTimer("store.read");
        try
        {
            return await _innerStore.GetAsync(key);
        }
        catch
        {
            _metrics.IncrementCounter("store.errors");
            throw;
        }
    }
}
```

## Security Considerations

### Provider-Specific Security

1. **FileSystem**: Use OS-level permissions and encryption
2. **InMemory**: No persistence, data lost on restart
3. **ESENT**: Optional database encryption, Windows ACLs
4. **ClusterRegistry**: Cluster security, Kerberos authentication

### Data Protection Example
```csharp
public class EncryptedStore<T> : IStore<T> where T : class
{
    private readonly IStore<T> _innerStore;
    private readonly IDataProtector _protector;
    
    public async Task SaveAsync(string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        var encrypted = _protector.Protect(json);
        await _innerStore.SaveAsync(key, encrypted);
    }
}
```

## Future Providers

The architecture supports easy addition of new providers:

- **SQLite Provider**: Cross-platform relational storage
- **Redis Provider**: Distributed caching and persistence
- **Azure Cosmos DB Provider**: Global distribution
- **PostgreSQL Provider**: Enterprise relational database
- **MongoDB Provider**: Document-oriented NoSQL

## Getting Help

- Review individual provider documentation for detailed usage
- Check the [troubleshooting guide](./troubleshooting.md)
- Submit issues on [GitHub](https://github.com/reliablestore/issues)
- Join our [community discussions](https://github.com/reliablestore/discussions)