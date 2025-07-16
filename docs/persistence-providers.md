# ReliableStore Persistence Providers Guide

## Overview

ReliableStore offers a flexible, provider-based persistence architecture that allows you to choose the optimal storage backend for your specific requirements. All providers implement the same interfaces, making it easy to switch between providers or use different providers for different entity types.

## Latest Benchmark Analysis (July 2025)

Based on extensive benchmarking across multiple payload sizes (10KB - 5MB), we have updated recommendations for persistence provider selection.

### Executive Summary

**FileSystem** is now the recommended persistence provider for applications with variable payload sizes. It offers the best balance of performance, scalability, and reliability across all tested scenarios.

### TL;DR - Provider Selection

✅ **Use FileSystem for:**
- General purpose storage (RECOMMENDED)
- Variable payload sizes (10KB - 5MB)
- Cross-platform deployments
- Best performance/reliability balance

✅ **Use SQLite when:**
- ACID transactions are required
- Can accept 2x slower performance
- Need SQL query capabilities

⚠️ **Use ESENT only when:**
- Windows-only deployment
- Guaranteed small payloads (<100KB)
- Can enforce payload size limits

❌ **Avoid:**
- ClusterRegistry (failed in benchmarks)
- ESENT for large payloads (25-30s operations)
- SQL Server (poorest performance)

### Key Findings from Recent Benchmarks

1. **ClusterRegistry Reliability Issues**
   - Failed frequently under pressure (many NA results in benchmarks)
   - Particularly unreliable with medium and large payloads
   - GetAll operations frequently failed
   - Not recommended for production use

2. **ESENT Performance Degradation**
   - Excellent for small payloads (competitive with FileSystem)
   - Performance degrades exponentially with payload size
   - Large payload performance is unacceptable (25-30 seconds for writes)
   - Only viable for applications with consistently small payloads

3. **FileSystem Consistency**
   - Most consistent performer across all payload sizes
   - Linear performance scaling with payload size
   - Best choice for large payloads
   - Good memory efficiency relative to performance

4. **SQLite Stability**
   - Consistent but slower than FileSystem
   - About 2x slower for large payloads
   - Better transactional guarantees than FileSystem
   - Good alternative when ACID compliance is critical

## Available Providers

### 1. FileSystem Provider
**Best for**: Development, small-to-medium deployments, human-readable storage

- **Storage**: JSON files on local file system
- **Platform**: Cross-platform (Windows, Linux, macOS)
- **Performance**: 23-79ms for small data, scales to 327-1629ms for large data
- **Scalability**: Limited by file system (millions of files possible)
- **Use Cases**: Development, testing, production with moderate load, configuration storage

[Full Documentation →](../src/Common.Persistence.Providers.FileSystem/README.md)

### 2. InMemory Provider
**Best for**: Testing, caching, temporary data

- **Storage**: RAM (volatile)
- **Platform**: Cross-platform
- **Performance**: Ultra-fast (173-179 microseconds for any size)
- **Scalability**: Limited by available memory
- **Use Cases**: Unit tests, integration tests, caching layer, session storage

[Full Documentation →](../src/Common.Persistence.Providers.InMemory/README.md)

### 3. ESENT Provider
**Best for**: High-performance Windows applications, local databases

- **Storage**: Embedded database (ESENT)
- **Platform**: Windows only
- **Performance**: Good for small data, severe degradation with large payloads (up to 29s)
- **Scalability**: Up to 16TB per database
- **Use Cases**: Desktop applications, Windows services, local data stores

[Full Documentation →](../src/Common.Persistence.Providers.Esent/README.md)

### 4. ClusterRegistry Provider
**Best for**: High availability requirements with tiny data payloads

- **Storage**: Windows Failover Cluster Registry
- **Platform**: Windows Server with Failover Clustering
- **Performance**: Excellent for small values (1-2ms), degrades 1000x with large data
- **Scalability**: 2-64 nodes, severely limited by payload size
- **Use Cases**: Small configuration data, feature flags, service discovery

[Full Documentation →](../src/Common.Persistence.Providers.ClusterRegistry/README.md)

## Provider Comparison Matrix

| Feature | FileSystem | InMemory | ESENT | ClusterRegistry | SQL Server | SQLite |
|---------|------------|----------|-------|-----------------|------------|--------|
| **Persistence** | ✅ Yes | ❌ No | ✅ Yes | ✅ Yes | ✅ Yes | ✅ Yes |
| **Platform** | 🌍 Any | 🌍 Any | 🪟 Windows | 🪟 Windows Server | 🌍 Any | 🌍 Any |
| **Performance (Small)** | ⭐⭐⭐ Good | ⭐⭐⭐⭐⭐ Excellent | ⭐⭐⭐ Good | ⭐⭐⭐⭐ Very Good | ⭐⭐ Fair | ⭐⭐ Fair |
| **Performance (Large)** | ⭐⭐ Fair | ⭐⭐⭐⭐⭐ Excellent | ⭐ Poor† | ⭐ Poor‡ | ⭐ Very Poor§ | ⭐⭐ Fair |
| **Transactions** | ✅ Basic | ✅ Full | ✅ Full ACID | ✅ Batch only | ✅ Full ACID | ✅ Full ACID |
| **Concurrency** | 🔒 File locks | 🔒 Thread-safe | 🔒 Row-level | 🔒 Distributed | 🔒 Row-level | 🔒 Database-level |
| **Max Item Size** | 💾 2GB (practical) | 💾 RAM limit | 💾 2GB | 💾 1MB¹ | 💾 2GB | 💾 1GB |
| **Max Database Size** | 💾 OS limit | 💾 RAM | 💾 16TB | 💾 Registry limit² | 💾 524TB | 💾 281TB |
| **Query Support** | ❌ None | ❌ None | ✅ Indexed | ❌ None | ✅ Full SQL | ✅ Full SQL |
| **High Availability** | ❌ No | ❌ No | ❌ No | ✅ Automatic | ✅ With clustering | ❌ No |
| **Backup** | 📁 File copy | 📸 Snapshot | 💾 Online | 📋 Export | 💾 Full featured | 📁 File copy |
| **Setup Complexity** | ⭐ Simple | ⭐ None | ⭐⭐ Moderate | ⭐⭐⭐⭐ Complex | ⭐⭐⭐ Complex | ⭐ Simple |
| **Memory Usage** | 🔸 Moderate | 🔹 Minimal | 🔸 Moderate | 🔺 High³ | 🔺 High | 🔸 Moderate |

**Performance Notes:**
- † ESENT: Up to 29 seconds for large payload operations
- ‡ ClusterRegistry: 1000x performance degradation from small to large payloads
- § SQL Server: Up to 56 seconds for large payload reads
- ¹ ClusterRegistry: Microsoft recommends <2KB for registry values
- ² ClusterRegistry: Limited by Windows registry constraints
- ³ ClusterRegistry: Up to 25GB memory for 5MB payloads

### Performance Comparison

Based on recent benchmarks (Windows 11, .NET 9.0.7, X64 RyuJIT AVX-512, July 2025):

| Provider | Small Payload (10KB) | Medium Payload (100KB) | Large Payload (5MB) | Memory Allocation |
|----------|---------------------|------------------------|---------------------|-------------------|
| **InMemory** | **173-179 μs** ✅ | **173-179 μs** ✅ | **173-179 μs** ✅ | **8-12 KB** ✅ |
| **FileSystem** | 55-60 ms | 80-83 ms | 650 ms | 773 KB → 1.22 GB |
| **SQLite** | 600-700 ms | 620-640 ms | 1,400 ms | 645 KB → 1.22 GB |
| **ESENT** | 103 ms | 490-560 ms | **25-30 seconds** ❌ | 536 KB → 1.22 GB |
| **ClusterRegistry** | 16 ms | 42 ms ⚠️ | **NA (Failed)** ❌ | 550 KB → 1.22 GB |
| **SQL Server** | 11-82 ms | 103-158 ms | **728ms-56s** ❌ | Very High |

#### Detailed Performance by Operation Type (July 2025 Benchmarks)

| Operation | InMemory | FileSystem | SQLite | ESENT | ClusterRegistry |
|-----------|----------|------------|--------|-------|-----------------|
| **Sequential Writes** | 173 μs | 55 ms → 650 ms | 600 ms → 1.4s | 103 ms → 25-30s | 16 ms → NA |
| **Sequential Reads** | 178 μs | 79 ms → 1.6s | 610 ms → 2.5s | 103 ms → 24-29s | 36 ms → NA |
| **Mixed Operations** | 73 μs | 62 ms → 1.15s | 480 ms → 1.9s | 80 ms → 20-27s | 29 ms → NA |
| **Batch Operations** | 175 μs | 57 ms → 575 ms | 610 ms → 1.4s | 104 ms → 25-30s | 16 ms → NA |
| **GetAll Operations** | 179 μs | 81 ms → 890 ms | 640 ms → 2.5s | 103 ms → 24-29s | NA → NA |

*Note: Times shown as "small payload → large payload"*

### Updated Recommendations (July 2025)

Based on the latest benchmark data:

1. **Best Overall: FileSystem**
   - Consistent performance across all payload sizes
   - Linear scaling characteristics
   - No catastrophic performance degradation
   - Recommended for variable payload applications

2. **Alternative: SQLite**
   - 2x slower than FileSystem but stable
   - Better transactional guarantees
   - Good for ACID compliance requirements

3. **Special Cases Only:**
   - **InMemory**: Testing and caching only
   - **ESENT**: Small payloads only (<100KB)
   - **ClusterRegistry**: Not recommended due to failures

⚠️ **Critical Performance Notes**:
- ClusterRegistry showed multiple failures (NA results) in benchmarks
- ESENT becomes unusable for large payloads (25-30 seconds per operation)
- FileSystem provides the best balance of performance and reliability

## Choosing the Right Provider

### Decision Tree (Updated July 2025)

```
Start → Is data temporary/cache only?
         ├─ Yes → InMemory Provider
         └─ No → Need persistent storage
                  ↓
         What are your primary requirements?
         ├─ Performance + Reliability → FileSystem Provider ⭐ RECOMMENDED
         ├─ ACID Transactions → SQLite Provider
         ├─ Windows + Small data only (<100KB) → ESENT Provider
         └─ High Availability → FileSystem + External Replication
```

### Quick Decision Guide

| Scenario | Recommended Provider | Why |
|----------|---------------------|-----|
| **General Purpose** | **FileSystem** | Best overall performance, scales linearly |
| **Variable Payloads (10KB-5MB)** | **FileSystem** | Consistent performance across all sizes |
| **Need ACID Compliance** | **SQLite** | Full transactional support, 2x slower but reliable |
| **Testing/Development** | **InMemory** | Ultra-fast, no persistence overhead |
| **Windows + Small Data Only** | **ESENT** | Good for <100KB, fails at larger sizes |
| **High Availability** | **FileSystem + DFS-R/rsync** | ClusterRegistry proved unreliable |

**⚠️ Providers to Avoid:**
- **ClusterRegistry**: Multiple failures in benchmarks, unreliable
- **ESENT for large data**: 25-30 second operations unacceptable
- **SQL Server**: Poorest performance, especially for reads

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

### Use Case Recommendations (Updated July 2025)

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

#### Production - Recommended Approach
```csharp
// FileSystem is now the recommended provider for all production scenarios
services.AddSingleton<IStore<Product>>(
    new FileStore<Product>("products", new FileSystemStoreSettings
    {
        BasePath = "/var/data/reliablestore"  // Linux/macOS
        // OR
        BasePath = @"C:\Data\ReliableStore"   // Windows
    }));

// When ACID transactions are required
services.AddSingleton<IStore<Order>>(
    new SQLiteProvider<Order>(new SQLiteStoreSettings
    {
        DatabasePath = "data/orders.db",
        ConnectionString = "Data Source=orders.db;Version=3;"
    }));
```

#### Special Cases Only
```csharp
// ESENT - Only for Windows with consistently small payloads (<100KB)
// ⚠️ WARNING: Performance degrades 250x with large payloads
services.AddSingleton<IStore<Config>>(
    new SimpleEsentProvider<Config>(new EsentStoreSettings
    {
        DatabasePath = "data/config.edb",
        CacheSizeMB = 128,
        MaxItemSizeKB = 100  // Enforce size limit
    }));

// ❌ AVOID ClusterRegistry - Unreliable in production
// Multiple failures observed in benchmarks
// Use FileSystem + external replication instead
```

#### High Availability Production
```csharp
// Recommended: FileSystem + External replication
services.AddSingleton<IStore<Product>>(
    new FileStore<Product>("products", new FileSystemStoreSettings
    {
        BasePath = @"D:\ReplicatedData"  // Replicated via DFS-R, rsync, etc.
    }));

// Alternative: SQLite + Litestream for replication
services.AddSingleton<IStore<Order>>(
    new SQLiteProvider<Order>(new SQLiteStoreSettings
    {
        DatabasePath = "/data/orders.db"  // Replicated by Litestream
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

### 2. Multi-Provider Strategy (Updated July 2025)
```csharp
// Use different providers based on data characteristics
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Volatile session data in memory
        services.AddSingleton<IStore<Session>>(
            new InMemoryStore<Session>());

        // Product catalog - FileSystem for best performance
        services.AddSingleton<IStore<Product>>(
            new FileStore<Product>("products", fileSettings));

        // Orders - SQLite for transactional integrity
        services.AddSingleton<IStore<Order>>(
            new SQLiteProvider<Order>(sqliteSettings));

        // Small config (<100KB) - ESENT if Windows-only
        services.AddSingleton<IStore<ServiceConfig>>(
            new SimpleEsentProvider<ServiceConfig>(esentSettings));

        // Audit logs - FileSystem for reliability and performance
        services.AddSingleton<IStore<AuditLog>>(
            new FileStore<AuditLog>("audit", fileSettings));
        
        // ❌ ClusterRegistry removed - use FileSystem instead
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

## Performance Summary (Updated July 2025)

### Performance Rankings (Best to Worst)

1. **InMemory** - Ultra-fast microsecond performance, ideal for caching
2. **FileSystem** - Best overall performer for real persistence scenarios
3. **SQLite** - Consistent performance, 2x slower than FileSystem but reliable
4. **ESENT** - Good for small data only, catastrophic for large payloads
5. **SQL Server** - Poor performance, especially for large data reads
6. **ClusterRegistry** - Not recommended due to failures in production benchmarks

### Key Insights from July 2025 Benchmarks

- **FileSystem** is the clear winner for variable payload sizes (10KB-5MB)
  - Linear performance scaling: 55ms (small) → 650ms (large)
  - No catastrophic degradation
  - Consistent across all operation types
  
- **SQLite** serves as a reliable alternative
  - Approximately 2x slower than FileSystem
  - Better ACID guarantees
  - No performance cliffs
  
- **ESENT** should be avoided for variable payloads
  - Performance degrades 250x from small to large payloads
  - 25-30 second operations make it unusable for large data
  
- **ClusterRegistry** showed critical reliability issues
  - Multiple NA (failure) results in benchmarks
  - Cannot be trusted for production use

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