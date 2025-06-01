# Common.Persistence - Persistence Abstraction Layer Design

## Overview

Common.Persistence provides a transaction-agnostic persistence abstraction layer designed to support multiple storage backends while maintaining high performance, concurrency safety, consistency, and reliability in distributed environments. The library acts as an adapter that enables applications to persist data across various storage technologies without coupling to specific implementations.

## Design Principles

### Transaction Agnostic
- **No Transaction Awareness**: Persistence layer has no knowledge of transactions
- **Stateless Operations**: Each operation is independent and atomic at the storage level
- **Transaction Integration**: Higher-level transaction management (Common.Tx) coordinates multiple operations
- **Resource Interface**: Implements `ITransactionalResource` for transaction participation without internal transaction logic

### Storage Abstraction
- **Multiple Backend Support**: Cache, cluster databases, relational databases, file systems, in-memory stores
- **Consistent Interface**: Uniform API across all storage implementations
- **Provider Pattern**: Pluggable storage providers with runtime selection
- **Configuration-Driven**: Storage selection and configuration through dependency injection

### Performance First
- **Async/Await**: Full asynchronous operation support
- **Batch Operations**: Efficient bulk operations for high throughput
- **Caching Strategies**: Configurable caching layers for performance optimization
- **Connection Pooling**: Efficient resource utilization across storage backends

### Distributed Environment Ready
- **Concurrency Safety**: Thread-safe operations with appropriate locking strategies
- **Consistency Models**: Support for various consistency levels (eventual, strong, session)
- **Fault Tolerance**: Resilient operation with automatic retry and circuit breaker patterns
- **Scalability**: Horizontal scaling support through partitioning and sharding

## Core Architecture

### Abstraction Layers

```
Application Layer
       ↓
IRepository<T> Interface
       ↓
Storage Provider Layer
       ↓
Backend-Specific Implementations
       ↓
Storage Systems (File, DB, Cache, etc.)
```

### Key Components

1. **Repository Interface** (`IRepository<T>`) - Unified CRUD operations
2. **Storage Providers** - Backend-specific implementations
3. **Configuration System** - Provider selection and configuration
4. **Connection Management** - Resource pooling and lifecycle management
5. **Serialization Layer** - Pluggable serialization strategies
6. **Caching Layer** - Multi-level caching with invalidation strategies
7. **Health Monitoring** - Provider health checks and circuit breaker patterns

## Storage Provider Implementations

### File System Provider (`FileSystemProvider<T>`)

**Use Cases**: Development, small datasets, audit logs, configuration storage

**Design Features:**
- **Atomic Operations**: Temp file + rename pattern for consistency
- **Directory Structure**: Configurable partitioning (by date, hash, etc.)
- **File Formats**: JSON, XML, Binary, Custom serialization
- **Locking Strategy**: File-level advisory locking
- **Backup Integration**: Configurable backup retention policies

```csharp
public class FileSystemProvider<T> : IStorageProvider<T>
{
    private readonly FileSystemOptions _options;
    private readonly ISerializer<T> _serializer;
    private readonly IFileLockManager _lockManager;

    // Atomic write with temp file + rename
    public async Task SaveAsync(string key, T entity, CancellationToken cancellationToken = default)
    {
        var tempFile = GetTempFilePath(key);
        var targetFile = GetFilePath(key);

        await _serializer.SerializeToFileAsync(entity, tempFile, cancellationToken);
        File.Move(tempFile, targetFile); // Atomic on same filesystem
    }
}
```

### In-Memory Provider (`InMemoryProvider<T>`)

**Use Cases**: Testing, caching, session storage, temporary data

**Design Features:**
- **Thread Safety**: `ConcurrentDictionary` for safe concurrent access
- **Memory Management**: Configurable eviction policies (LRU, TTL, size-based)
- **Partitioning**: Memory-based sharding for large datasets
- **Durability Options**: Optional persistence to disk for recovery

```csharp
public class InMemoryProvider<T> : IStorageProvider<T>
{
    private readonly ConcurrentDictionary<string, CacheEntry<T>> _cache;
    private readonly MemoryOptions _options;
    private readonly IEvictionStrategy _evictionStrategy;

    public async Task<T?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            entry.UpdateLastAccessed();
            return entry.Value;
        }
        return default(T);
    }
}
```

### SQL Database Provider (`SqlDatabaseProvider<T>`)

**Use Cases**: Production applications, complex queries, ACID guarantees, reporting

**Design Features:**
- **Connection Pooling**: Efficient database connection management
- **Query Optimization**: Prepared statements and indexed queries
- **Schema Management**: Automatic table creation and migrations
- **Bulk Operations**: Efficient batch inserts/updates using SQL bulk operations
- **Multi-Database Support**: SQL Server, PostgreSQL, MySQL, SQLite

```csharp
public class SqlDatabaseProvider<T> : IStorageProvider<T>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ISqlQueryBuilder<T> _queryBuilder;
    private readonly ISchemaManager<T> _schemaManager;

    public async Task SaveManyAsync(IEnumerable<KeyValuePair<string, T>> entities,
        CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var bulkCopy = _queryBuilder.CreateBulkCopy(connection);

        var dataTable = _schemaManager.ConvertToDataTable(entities);
        await bulkCopy.WriteToServerAsync(dataTable, cancellationToken);
    }
}
```

### NoSQL Database Provider (`NoSqlDatabaseProvider<T>`)

**Use Cases**: High scalability, document storage, schema flexibility, geographic distribution

**Design Features:**
- **Document Storage**: Native JSON/BSON document support
- **Horizontal Scaling**: Automatic sharding and partitioning
- **Eventual Consistency**: Configurable consistency levels
- **Multi-Database Support**: MongoDB, Cosmos DB, DynamoDB, Cassandra

```csharp
public class NoSqlDatabaseProvider<T> : IStorageProvider<T>
{
    private readonly INoSqlClient _client;
    private readonly NoSqlOptions _options;
    private readonly IPartitionStrategy _partitionStrategy;

    public async Task<IEnumerable<T>> GetAllAsync(Expression<Func<T, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        var partitions = await _partitionStrategy.GetActivePartitionsAsync();
        var tasks = partitions.Select(p => QueryPartitionAsync(p, predicate, cancellationToken));
        var results = await Task.WhenAll(tasks);

        return results.SelectMany(r => r);
    }
}
```

### Cache Provider (`CacheProvider<T>`)

**Use Cases**: High-performance caching, session storage, distributed caching

**Design Features:**
- **Distributed Caching**: Redis, Memcached, In-Memory distributed cache
- **Cache Strategies**: Write-through, write-behind, refresh-ahead
- **Serialization**: Efficient binary serialization for network transport
- **TTL Management**: Configurable time-to-live with sliding expiration

```csharp
public class CacheProvider<T> : IStorageProvider<T>
{
    private readonly IDistributedCache _cache;
    private readonly ICacheSerializer<T> _serializer;
    private readonly CacheOptions _options;

    public async Task SaveAsync(string key, T entity, CancellationToken cancellationToken = default)
    {
        var serialized = await _serializer.SerializeAsync(entity);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _options.DefaultTTL,
            SlidingExpiration = _options.SlidingExpiration
        };

        await _cache.SetAsync(GetCacheKey(key), serialized, options, cancellationToken);
    }
}
```

## Performance Considerations for Distributed Environments

### Caching Strategies

#### Multi-Level Caching Architecture
```
L1 Cache (In-Memory) → L2 Cache (Redis) → Primary Storage (Database/File)
```

**Implementation:**
- **L1 Cache**: Process-local cache for frequently accessed data
- **L2 Cache**: Distributed cache for cross-service data sharing
- **Cache Coherence**: Event-driven invalidation across cache levels
- **Cache Warming**: Proactive loading of critical data

#### Cache-Aside Pattern
```csharp
public async Task<T?> GetWithCacheAsync(string key, CancellationToken cancellationToken = default)
{
    // L1 Cache check
    if (_l1Cache.TryGetValue(key, out var cached))
        return cached;

    // L2 Cache check
    var l2Cached = await _l2Cache.GetAsync(key, cancellationToken);
    if (l2Cached != null)
    {
        _l1Cache.Set(key, l2Cached);
        return l2Cached;
    }

    // Primary storage
    var entity = await _primaryStorage.GetAsync(key, cancellationToken);
    if (entity != null)
    {
        await _l2Cache.SetAsync(key, entity, cancellationToken);
        _l1Cache.Set(key, entity);
    }

    return entity;
}
```

### Batch Operations and Bulk Processing

#### Chunked Batch Processing
```csharp
public async Task SaveManyAsync(IEnumerable<KeyValuePair<string, T>> entities,
    CancellationToken cancellationToken = default)
{
    const int chunkSize = 1000;
    var chunks = entities.Chunk(chunkSize);

    var semaphore = new SemaphoreSlim(_options.MaxConcurrentBatches);
    var tasks = chunks.Select(async chunk =>
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            await ProcessChunkAsync(chunk, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    });

    await Task.WhenAll(tasks);
}
```

#### Connection Pooling and Resource Management
```csharp
public class ConnectionPool<TConnection> : IConnectionPool<TConnection>
    where TConnection : IDisposable
{
    private readonly ConcurrentQueue<TConnection> _connections;
    private readonly SemaphoreSlim _semaphore;
    private readonly ConnectionOptions _options;

    public async Task<PooledConnection<TConnection>> AcquireConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);

        if (_connections.TryDequeue(out var connection))
        {
            if (await IsConnectionValidAsync(connection))
                return new PooledConnection<TConnection>(connection, this);

            connection.Dispose();
        }

        connection = await CreateConnectionAsync(cancellationToken);
        return new PooledConnection<TConnection>(connection, this);
    }
}
```

### Partitioning and Sharding Strategies

#### Hash-Based Partitioning
```csharp
public class HashPartitionStrategy : IPartitionStrategy
{
    private readonly int _partitionCount;

    public string GetPartition(string key)
    {
        var hash = key.GetHashCode();
        var partition = Math.Abs(hash) % _partitionCount;
        return $"partition_{partition:D3}";
    }

    public async Task<IEnumerable<T>> GetAllAsync(Expression<Func<T, bool>>? predicate = null)
    {
        var partitionTasks = Enumerable.Range(0, _partitionCount)
            .Select(i => GetPartitionDataAsync($"partition_{i:D3}", predicate));

        var results = await Task.WhenAll(partitionTasks);
        return results.SelectMany(r => r);
    }
}
```

## Concurrency and Consistency Patterns

### Optimistic Concurrency Control

#### Version-Based Concurrency
```csharp
public class VersionedEntity<T>
{
    public T Entity { get; set; }
    public long Version { get; set; }
    public DateTime LastModified { get; set; }
    public string? ETag { get; set; }
}

public async Task SaveAsync(string key, T entity, long? expectedVersion = null,
    CancellationToken cancellationToken = default)
{
    var current = await GetVersionedAsync(key, cancellationToken);

    if (expectedVersion.HasValue && current?.Version != expectedVersion.Value)
        throw new OptimisticConcurrencyException($"Version mismatch for key {key}");

    var newVersion = (current?.Version ?? 0) + 1;
    var versioned = new VersionedEntity<T>
    {
        Entity = entity,
        Version = newVersion,
        LastModified = DateTime.UtcNow,
        ETag = GenerateETag(entity, newVersion)
    };

    await SaveVersionedAsync(key, versioned, cancellationToken);
}
```

#### Lock-Free Operations
```csharp
public class LockFreeCounter
{
    private long _value;

    public long Increment() => Interlocked.Increment(ref _value);
    public long Add(long value) => Interlocked.Add(ref _value, value);
    public bool CompareAndSwap(long expected, long newValue)
        => Interlocked.CompareExchange(ref _value, newValue, expected) == expected;
}
```

### Consistency Models

#### Eventual Consistency with Conflict Resolution
```csharp
public class EventuallyConsistentProvider<T> : IStorageProvider<T>
{
    private readonly IConflictResolver<T> _conflictResolver;
    private readonly IReplicationManager _replicationManager;

    public async Task SaveAsync(string key, T entity, CancellationToken cancellationToken = default)
    {
        // Save to primary node
        await _primaryNode.SaveAsync(key, entity, cancellationToken);

        // Async replication to secondary nodes
        _ = Task.Run(async () =>
        {
            await _replicationManager.ReplicateAsync(key, entity, cancellationToken);
        }, cancellationToken);
    }

    public async Task<T?> GetAsync(string key, ConsistencyLevel consistency = ConsistencyLevel.Eventual,
        CancellationToken cancellationToken = default)
    {
        return consistency switch
        {
            ConsistencyLevel.Strong => await GetFromPrimaryAsync(key, cancellationToken),
            ConsistencyLevel.Session => await GetFromSessionConsistentNodeAsync(key, cancellationToken),
            ConsistencyLevel.Eventual => await GetFromAnyNodeAsync(key, cancellationToken),
            _ => throw new ArgumentException($"Unsupported consistency level: {consistency}")
        };
    }
}
```

### Thread Safety Patterns

#### Reader-Writer Lock for Cache Operations
```csharp
public class ThreadSafeCacheProvider<T> : IStorageProvider<T>
{
    private readonly Dictionary<string, T> _cache = new();
    private readonly ReaderWriterLockSlim _lock = new();

    public async Task<T?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        _lock.EnterReadLock();
        try
        {
            return _cache.TryGetValue(key, out var value) ? value : default(T);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public async Task SaveAsync(string key, T entity, CancellationToken cancellationToken = default)
    {
        _lock.EnterWriteLock();
        try
        {
            _cache[key] = entity;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
}
```

## Reliability and Fault Tolerance

### Circuit Breaker Pattern

```csharp
public class CircuitBreakerProvider<T> : IStorageProvider<T>
{
    private readonly IStorageProvider<T> _innerProvider;
    private readonly CircuitBreakerOptions _options;
    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _failureCount = 0;
    private DateTime _lastFailureTime = DateTime.MinValue;

    public async Task<T?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_state == CircuitBreakerState.Open)
        {
            if (DateTime.UtcNow - _lastFailureTime > _options.RetryTimeout)
                _state = CircuitBreakerState.HalfOpen;
            else
                throw new CircuitBreakerOpenException("Circuit breaker is open");
        }

        try
        {
            var result = await _innerProvider.GetAsync(key, cancellationToken);
            OnSuccess();
            return result;
        }
        catch (Exception ex)
        {
            OnFailure(ex);
            throw;
        }
    }

    private void OnSuccess()
    {
        _failureCount = 0;
        _state = CircuitBreakerState.Closed;
    }

    private void OnFailure(Exception ex)
    {
        _failureCount++;
        _lastFailureTime = DateTime.UtcNow;

        if (_failureCount >= _options.FailureThreshold)
            _state = CircuitBreakerState.Open;
    }
}
```

### Retry Mechanisms with Exponential Backoff

```csharp
public class RetryProvider<T> : IStorageProvider<T>
{
    private readonly IStorageProvider<T> _innerProvider;
    private readonly RetryOptions _options;

    public async Task SaveAsync(string key, T entity, CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        var delay = _options.InitialDelay;

        while (attempt < _options.MaxRetries)
        {
            try
            {
                await _innerProvider.SaveAsync(key, entity, cancellationToken);
                return;
            }
            catch (Exception ex) when (ShouldRetry(ex, attempt))
            {
                attempt++;
                if (attempt >= _options.MaxRetries)
                    throw;

                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * _options.BackoffMultiplier);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds, _options.MaxDelay.TotalMilliseconds));
            }
        }
    }

    private static bool ShouldRetry(Exception ex, int attempt)
    {
        return ex is TimeoutException or SocketException or HttpRequestException;
    }
}
```

### Health Monitoring and Diagnostics

```csharp
public class HealthMonitoringProvider<T> : IStorageProvider<T>
{
    private readonly IStorageProvider<T> _innerProvider;
    private readonly IHealthReporter _healthReporter;
    private readonly IMetrics _metrics;

    public async Task<T?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        using var activity = _metrics.StartActivity("storage.get");
        activity?.SetTag("key", key);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await _innerProvider.GetAsync(key, cancellationToken);

            _metrics.RecordValue("storage.get.duration", stopwatch.ElapsedMilliseconds);
            _metrics.RecordValue("storage.get.success", 1);
            _healthReporter.ReportSuccess("storage.get");

            return result;
        }
        catch (Exception ex)
        {
            _metrics.RecordValue("storage.get.duration", stopwatch.ElapsedMilliseconds);
            _metrics.RecordValue("storage.get.failure", 1);
            _healthReporter.ReportFailure("storage.get", ex);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
```

## Configuration and Dependency Injection

### Provider Configuration

```csharp
public class PersistenceOptions
{
    public string DefaultProvider { get; set; } = "FileSystem";
    public Dictionary<string, ProviderConfiguration> Providers { get; set; } = new();
    public CacheConfiguration? Cache { get; set; }
    public RetryConfiguration? Retry { get; set; }
    public CircuitBreakerConfiguration? CircuitBreaker { get; set; }
}

public class ProviderConfiguration
{
    public string Type { get; set; } = "";
    public Dictionary<string, object> Settings { get; set; } = new();
    public int Priority { get; set; } = 0;
    public bool Enabled { get; set; } = true;
}
```

### Service Registration

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services,
        Action<PersistenceOptions>? configureOptions = null)
    {
        services.Configure<PersistenceOptions>(configureOptions ?? (_ => { }));

        // Core services
        services.AddSingleton<IProviderFactory, ProviderFactory>();
        services.AddSingleton<ISerializerFactory, SerializerFactory>();
        services.AddSingleton<IHealthReporter, HealthReporter>();

        // Provider implementations
        services.AddTransient<FileSystemProvider<object>>();
        services.AddTransient<InMemoryProvider<object>>();
        services.AddTransient<SqlDatabaseProvider<object>>();
        services.AddTransient<NoSqlDatabaseProvider<object>>();
        services.AddTransient<CacheProvider<object>>();

        // Decorators (applied in order)
        services.Decorate<IStorageProvider<object>, HealthMonitoringProvider<object>>();
        services.Decorate<IStorageProvider<object>, RetryProvider<object>>();
        services.Decorate<IStorageProvider<object>, CircuitBreakerProvider<object>>();

        return services;
    }

    public static IServiceCollection AddPersistenceForEntity<T>(this IServiceCollection services,
        string? providerName = null)
    {
        services.AddTransient<IRepository<T>>(provider =>
        {
            var factory = provider.GetRequiredService<IProviderFactory>();
            var storageProvider = factory.CreateProvider<T>(providerName);
            return new Repository<T>(storageProvider);
        });

        return services;
    }
}
```

## Comprehensive Test Strategy

### Unit Testing Strategy

#### Provider-Specific Tests
```csharp
[TestClass]
public abstract class StorageProviderTestBase<T> where T : class, new()
{
    protected abstract IStorageProvider<T> CreateProvider();
    protected abstract T CreateTestEntity(string id);
    protected abstract void AssertEntitiesEqual(T expected, T actual);

    [TestMethod]
    public async Task SaveAsync_ValidEntity_SavesSuccessfully()
    {
        // Arrange
        var provider = CreateProvider();
        var entity = CreateTestEntity("test-id");

        // Act
        await provider.SaveAsync("test-key", entity);

        // Assert
        var retrieved = await provider.GetAsync("test-key");
        Assert.IsNotNull(retrieved);
        AssertEntitiesEqual(entity, retrieved);
    }

    [TestMethod]
    public async Task GetAsync_NonExistentKey_ReturnsNull()
    {
        // Arrange
        var provider = CreateProvider();

        // Act
        var result = await provider.GetAsync("non-existent-key");

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task SaveManyAsync_LargeDataset_HandlesEfficiently()
    {
        // Arrange
        var provider = CreateProvider();
        var entities = Enumerable.Range(0, 10000)
            .Select(i => new KeyValuePair<string, T>($"key-{i}", CreateTestEntity($"id-{i}")))
            .ToList();

        // Act
        var stopwatch = Stopwatch.StartNew();
        await provider.SaveManyAsync(entities);
        stopwatch.Stop();

        // Assert
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 5000, "Bulk operation took too long");

        var retrieved = await provider.GetManyAsync(entities.Select(e => e.Key));
        Assert.AreEqual(entities.Count, retrieved.Count());
    }
}

[TestClass]
public class FileSystemProviderTests : StorageProviderTestBase<TestEntity>
{
    protected override IStorageProvider<TestEntity> CreateProvider()
    {
        var tempPath = Path.GetTempPath();
        var options = new FileSystemOptions { BasePath = tempPath };
        return new FileSystemProvider<TestEntity>(options, new JsonSerializer<TestEntity>());
    }

    protected override TestEntity CreateTestEntity(string id) => new() { Id = id, Name = $"Test-{id}" };

    protected override void AssertEntitiesEqual(TestEntity expected, TestEntity actual)
    {
        Assert.AreEqual(expected.Id, actual.Id);
        Assert.AreEqual(expected.Name, actual.Name);
    }
}
```

#### Concurrency Testing
```csharp
[TestClass]
public class ConcurrencyTests
{
    [TestMethod]
    public async Task ConcurrentOperations_MultipleThreads_NoDataCorruption()
    {
        // Arrange
        var provider = new InMemoryProvider<CounterEntity>();
        const int threadCount = 10;
        const int operationsPerThread = 1000;

        // Act
        var tasks = Enumerable.Range(0, threadCount).Select(async threadId =>
        {
            for (int i = 0; i < operationsPerThread; i++)
            {
                var key = $"counter-{threadId}";
                var current = await provider.GetAsync(key) ?? new CounterEntity { Value = 0 };
                current.Value++;
                await provider.SaveAsync(key, current);
            }
        });

        await Task.WhenAll(tasks);

        // Assert
        for (int i = 0; i < threadCount; i++)
        {
            var counter = await provider.GetAsync($"counter-{i}");
            Assert.IsNotNull(counter);
            Assert.AreEqual(operationsPerThread, counter.Value);
        }
    }

    [TestMethod]
    public async Task OptimisticConcurrency_ConflictingUpdates_ThrowsException()
    {
        // Arrange
        var provider = new VersionedProvider<TestEntity>();
        var entity = new TestEntity { Id = "test", Name = "Original" };
        await provider.SaveAsync("test", entity);

        // Act & Assert
        var entity1 = await provider.GetVersionedAsync("test");
        var entity2 = await provider.GetVersionedAsync("test");

        entity1!.Entity.Name = "Updated1";
        entity2!.Entity.Name = "Updated2";

        await provider.SaveAsync("test", entity1.Entity, entity1.Version);

        await Assert.ThrowsExceptionAsync<OptimisticConcurrencyException>(async () =>
            await provider.SaveAsync("test", entity2.Entity, entity2.Version));
    }
}
```

### Integration Testing Strategy

#### Provider Integration Tests
```csharp
[TestClass]
public class ProviderIntegrationTests
{
    [TestMethod]
    public async Task MultiProvider_FailoverScenario_ContinuesOperation()
    {
        // Arrange
        var primaryProvider = new Mock<IStorageProvider<TestEntity>>();
        var fallbackProvider = new InMemoryProvider<TestEntity>();

        primaryProvider.Setup(p => p.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Primary provider unavailable"));

        var failoverProvider = new FailoverProvider<TestEntity>(primaryProvider.Object, fallbackProvider);

        // Act
        var entity = new TestEntity { Id = "test", Name = "Test Entity" };
        await fallbackProvider.SaveAsync("test", entity); // Pre-populate fallback

        var result = await failoverProvider.GetAsync("test");

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("test", result.Id);
        Assert.AreEqual("Test Entity", result.Name);
    }

    [TestMethod]
    public async Task CacheProvider_WriteThrough_UpdatesBothCacheAndStorage()
    {
        // Arrange
        var cache = new InMemoryProvider<TestEntity>();
        var storage = new InMemoryProvider<TestEntity>();
        var provider = new CachingProvider<TestEntity>(cache, storage, CacheStrategy.WriteThrough);

        // Act
        var entity = new TestEntity { Id = "test", Name = "Cached Entity" };
        await provider.SaveAsync("test", entity);

        // Assert
        var fromCache = await cache.GetAsync("test");
        var fromStorage = await storage.GetAsync("test");

        Assert.IsNotNull(fromCache);
        Assert.IsNotNull(fromStorage);
        Assert.AreEqual(entity.Name, fromCache.Name);
        Assert.AreEqual(entity.Name, fromStorage.Name);
    }
}
```

### Performance Testing Strategy

#### Benchmark Tests
```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class ProviderBenchmarks
{
    private IStorageProvider<TestEntity> _fileProvider = null!;
    private IStorageProvider<TestEntity> _memoryProvider = null!;
    private IStorageProvider<TestEntity> _sqlProvider = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fileProvider = new FileSystemProvider<TestEntity>(new FileSystemOptions());
        _memoryProvider = new InMemoryProvider<TestEntity>();
        _sqlProvider = new SqlDatabaseProvider<TestEntity>(new SqlOptions());
    }

    [Benchmark]
    [Arguments(100)]
    [Arguments(1000)]
    [Arguments(10000)]
    public async Task FileProvider_BulkSave(int entityCount)
    {
        var entities = GenerateTestEntities(entityCount);
        await _fileProvider.SaveManyAsync(entities);
    }

    [Benchmark]
    [Arguments(100)]
    [Arguments(1000)]
    [Arguments(10000)]
    public async Task MemoryProvider_BulkSave(int entityCount)
    {
        var entities = GenerateTestEntities(entityCount);
        await _memoryProvider.SaveManyAsync(entities);
    }

    [Benchmark]
    [Arguments(100)]
    [Arguments(1000)]
    [Arguments(10000)]
    public async Task SqlProvider_BulkSave(int entityCount)
    {
        var entities = GenerateTestEntities(entityCount);
        await _sqlProvider.SaveManyAsync(entities);
    }

    private static IEnumerable<KeyValuePair<string, TestEntity>> GenerateTestEntities(int count)
    {
        return Enumerable.Range(0, count)
            .Select(i => new KeyValuePair<string, TestEntity>(
                $"key-{i}",
                new TestEntity { Id = $"id-{i}", Name = $"Entity-{i}" }));
    }
}
```

#### Load Testing
```csharp
[TestClass]
public class LoadTests
{
    [TestMethod]
    public async Task HighConcurrency_1000SimultaneousOperations_MaintainsPerformance()
    {
        // Arrange
        var provider = new SqlDatabaseProvider<TestEntity>(new SqlOptions());
        const int concurrentOperations = 1000;
        const int maxOperationTimeMs = 1000;

        // Act
        var stopwatch = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, concurrentOperations).Select(async i =>
        {
            var entity = new TestEntity { Id = $"load-test-{i}", Name = $"Load Test Entity {i}" };
            await provider.SaveAsync($"load-{i}", entity);
            return await provider.GetAsync($"load-{i}");
        });

        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        Assert.AreEqual(concurrentOperations, results.Length);
        Assert.IsTrue(results.All(r => r != null), "All operations should succeed");
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < maxOperationTimeMs * concurrentOperations / 10,
            "Average operation time should be reasonable under load");
    }

    [TestMethod]
    public async Task MemoryUsage_LargeDataset_StaysWithinLimits()
    {
        // Arrange
        var provider = new InMemoryProvider<TestEntity>();
        const int entityCount = 100000;
        const long maxMemoryMB = 500;

        var initialMemory = GC.GetTotalMemory(true);

        // Act
        var entities = Enumerable.Range(0, entityCount)
            .Select(i => new KeyValuePair<string, TestEntity>(
                $"memory-test-{i}",
                new TestEntity { Id = $"id-{i}", Name = $"Memory Test Entity {i}" }))
            .ToList();

        await provider.SaveManyAsync(entities);

        var finalMemory = GC.GetTotalMemory(true);
        var memoryUsedMB = (finalMemory - initialMemory) / (1024 * 1024);

        // Assert
        Assert.IsTrue(memoryUsedMB < maxMemoryMB,
            $"Memory usage ({memoryUsedMB} MB) exceeded limit ({maxMemoryMB} MB)");
    }
}
```

### Chaos Testing Strategy

#### Fault Injection Tests
```csharp
[TestClass]
public class ChaosTests
{
    [TestMethod]
    public async Task NetworkFailure_DuringOperation_HandlesGracefully()
    {
        // Arrange
        var faultyProvider = new FaultInjectionProvider<TestEntity>(
            new SqlDatabaseProvider<TestEntity>(new SqlOptions()),
            faultProbability: 0.3);

        var retryProvider = new RetryProvider<TestEntity>(faultyProvider, new RetryOptions
        {
            MaxRetries = 3,
            InitialDelay = TimeSpan.FromMilliseconds(100)
        });

        // Act & Assert
        var entity = new TestEntity { Id = "chaos-test", Name = "Chaos Entity" };

        // Should eventually succeed despite random failures
        await retryProvider.SaveAsync("chaos", entity);
        var retrieved = await retryProvider.GetAsync("chaos");

        Assert.IsNotNull(retrieved);
        Assert.AreEqual("chaos-test", retrieved.Id);
    }

    [TestMethod]
    public async Task DiskSpace_Exhaustion_FailsGracefully()
    {
        // Arrange
        var provider = new FileSystemProvider<TestEntity>(new FileSystemOptions
        {
            BasePath = "/tmp/chaos-test",
            MaxDiskUsage = 1024 * 1024 // 1MB limit
        });

        // Act & Assert
        var largeEntities = Enumerable.Range(0, 10000)
            .Select(i => new KeyValuePair<string, TestEntity>(
                $"large-{i}",
                new TestEntity { Id = $"id-{i}", Name = new string('X', 1000) }));

        await Assert.ThrowsExceptionAsync<DiskSpaceExhaustedException>(async () =>
            await provider.SaveManyAsync(largeEntities));
    }
}
```

## Implementation Roadmap

### Phase 1: Core Abstraction (4 weeks)
- [ ] Define core interfaces (`IRepository<T>`, `IStorageProvider<T>`)
- [ ] Implement provider factory and configuration system
- [ ] Create base provider implementations (In-Memory, File System)
- [ ] Add basic serialization support (JSON, Binary)
- [ ] Implement unit tests for core functionality

### Phase 2: Performance Optimization (3 weeks)
- [ ] Add caching layer with multiple strategies
- [ ] Implement connection pooling for database providers
- [ ] Add batch operation support
- [ ] Optimize serialization performance
- [ ] Create performance benchmarks

### Phase 3: Database Providers (4 weeks)
- [ ] Implement SQL database provider (SQL Server, PostgreSQL)
- [ ] Add NoSQL provider (MongoDB, Cosmos DB)
- [ ] Implement schema management and migrations
- [ ] Add query optimization features
- [ ] Create integration tests

### Phase 4: Reliability Features (3 weeks)
- [ ] Implement circuit breaker pattern
- [ ] Add retry mechanisms with exponential backoff
- [ ] Create health monitoring and diagnostics
- [ ] Add failover and redundancy support
- [ ] Implement chaos testing

### Phase 5: Advanced Features (2 weeks)
- [ ] Add distributed caching support (Redis)
- [ ] Implement optimistic concurrency control
- [ ] Add partitioning and sharding strategies
- [ ] Create comprehensive documentation
- [ ] Performance tuning and optimization

## Success Metrics

### Performance Targets
- **Throughput**: 10,000+ operations/second for in-memory providers
- **Latency**: <10ms p95 for cache operations, <100ms p95 for database operations
- **Memory Efficiency**: <50MB overhead for 100,000 cached entities
- **Scalability**: Linear performance scaling up to 1M entities

### Reliability Targets
- **Availability**: 99.9% uptime with proper failover mechanisms
- **Data Durability**: Zero data loss with proper transactional integration
- **Error Recovery**: Automatic recovery from transient failures within 30 seconds
- **Consistency**: Strong consistency for critical operations, eventual consistency for high-throughput scenarios

### Quality Targets
- **Code Coverage**: >90% test coverage across all providers
- **Documentation**: Complete API documentation with usage examples
- **Performance**: Comprehensive benchmarks and load testing
- **Compatibility**: Support for .NET 8+ and multiple database platforms