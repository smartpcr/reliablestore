# InMemory Persistence Provider

## Overview

The InMemory persistence provider is a high-performance, volatile storage implementation designed for testing, development, and caching scenarios. It stores all data in memory using thread-safe concurrent collections.

## Features

- **Ultra-fast Performance**: No I/O overhead, pure memory operations
- **Thread-safe Collections**: Built on `ConcurrentDictionary<TKey, TValue>`
- **Full Transaction Support**: ACID compliance through Common.Tx framework
- **Zero Configuration**: Works out of the box with no setup
- **Memory Snapshots**: Support for creating point-in-time copies
- **Event Notifications**: Optional change notification support

## Use Cases

### Primary Use Cases
- **Unit Testing**: Fast, isolated tests without external dependencies
- **Integration Testing**: Predictable behavior for test scenarios
- **Development**: Rapid prototyping without infrastructure setup
- **Caching Layer**: High-speed temporary storage

### When NOT to Use
- Production data that must survive process restarts
- Large datasets that exceed available memory
- Scenarios requiring data persistence
- Multi-process data sharing

## Configuration

### Basic Setup

```csharp
// Simple initialization - no configuration required
var store = new InMemoryStore<Product>();

// With initial data
var initialData = new Dictionary<string, Product>
{
    ["prod1"] = new Product { Id = "prod1", Name = "Widget" }
};
var store = new InMemoryStore<Product>(initialData);
```

### Advanced Configuration

```csharp
var options = new InMemoryStoreOptions
{
    EnableChangeTracking = true,     // Track modifications
    EnableSnapshots = true,          // Allow point-in-time copies
    MaxItemCount = 10000,           // Limit total items
    EvictionPolicy = EvictionPolicy.LRU  // Least recently used
};

var store = new InMemoryStore<Product>(options);
```

## Usage Examples

### Basic CRUD Operations

```csharp
// Create/Update
var product = new Product { Id = "123", Name = "Widget", Price = 9.99m };
await store.SaveAsync("123", product);

// Read
var retrieved = await store.GetAsync("123");

// Exists check
bool exists = await store.ExistsAsync("123");

// Delete
await store.DeleteAsync("123");

// Get all
var all = await store.GetAllAsync();

// Count
int count = await store.CountAsync();
```

### Batch Operations

```csharp
// Batch save
var products = new Dictionary<string, Product>
{
    ["p1"] = new Product { Id = "p1", Name = "Product 1" },
    ["p2"] = new Product { Id = "p2", Name = "Product 2" },
    ["p3"] = new Product { Id = "p3", Name = "Product 3" }
};
await store.SaveBatchAsync(products);

// Batch delete
await store.DeleteBatchAsync(new[] { "p1", "p2", "p3" });

// Clear all
await store.ClearAsync();
```

### Transaction Support

```csharp
using var transaction = transactionFactory.CreateTransaction();
transaction.EnlistResource(store);

try
{
    await store.SaveAsync("1", item1);
    await store.SaveAsync("2", item2);
    await store.DeleteAsync("3");
    
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

### Snapshots and Cloning

```csharp
// Create a snapshot
var snapshot = store.CreateSnapshot();

// Clone the store
var clone = store.Clone();

// Restore from snapshot
store.RestoreSnapshot(snapshot);
```

### Change Notifications

```csharp
// Subscribe to changes
store.ItemChanged += (sender, args) =>
{
    Console.WriteLine($"Item {args.Key} was {args.ChangeType}");
};

// Supported events
store.ItemAdded += OnItemAdded;
store.ItemUpdated += OnItemUpdated;
store.ItemDeleted += OnItemDeleted;
store.StoreCleared += OnStoreCleared;
```

## Performance Characteristics

| Operation | Time Complexity | Notes |
|-----------|----------------|-------|
| Get | O(1) | Dictionary lookup |
| Save | O(1) | Dictionary insert/update |
| Delete | O(1) | Dictionary remove |
| GetAll | O(n) | Returns all values |
| Count | O(1) | Cached count |
| Clear | O(n) | Clears all items |
| Clone | O(n) | Deep copy of data |

### Memory Usage

- Base overhead: ~1KB per store instance
- Per item: Size of key + Size of value + ~100 bytes overhead
- Concurrent dictionary overhead: ~20% of data size

## Testing with InMemory Provider

### Unit Test Example

```csharp
[Fact]
public async Task SaveProduct_Should_PersistInMemory()
{
    // Arrange
    var store = new InMemoryStore<Product>();
    var product = new Product { Id = "123", Name = "Test Product" };
    
    // Act
    await store.SaveAsync(product.Id, product);
    var retrieved = await store.GetAsync(product.Id);
    
    // Assert
    retrieved.Should().BeEquivalentTo(product);
}
```

### Test Fixtures

```csharp
public class InMemoryStoreFixture : IDisposable
{
    public InMemoryStore<T> CreateStore<T>() where T : class
    {
        return new InMemoryStore<T>();
    }
    
    public InMemoryStore<T> CreateStoreWithData<T>(
        Dictionary<string, T> data) where T : class
    {
        return new InMemoryStore<T>(data);
    }
    
    public void Dispose()
    {
        // Cleanup if needed
    }
}
```

## Limitations

1. **No Persistence**: All data lost on process termination
2. **Memory Constraints**: Limited by available RAM
3. **Single Process**: No cross-process data sharing
4. **No Query Support**: Only key-based lookups and full scans
5. **No Indexing**: No secondary indexes or complex queries

## Migration Strategies

### To FileSystem Provider

```csharp
// Export from InMemory
var allData = await inMemoryStore.GetAllAsync();

// Import to FileSystem
var fileStore = new FileStore<Product>("products", settings);
foreach (var kvp in allData)
{
    await fileStore.SaveAsync(kvp.Key, kvp.Value);
}
```

### To Database Provider

```csharp
// Bulk export for database import
var items = await inMemoryStore.GetAllAsync();
await databaseStore.BulkInsertAsync(items.Values);
```

## Best Practices

1. **Testing**: Always use InMemory for unit tests
2. **Isolation**: Create new instances for each test
3. **Memory Management**: Monitor memory usage for large datasets
4. **Dispose Pattern**: Implement IDisposable if holding large data
5. **Thread Safety**: Rely on built-in concurrency support

## Advanced Scenarios

### Memory Pressure Handling

```csharp
public class MemoryAwareStore<T> : InMemoryStore<T> where T : class
{
    protected override async Task OnMemoryPressure()
    {
        // Evict least recently used items
        var itemsToEvict = GetLeastRecentlyUsed(100);
        foreach (var key in itemsToEvict)
        {
            await DeleteAsync(key);
        }
    }
}
```

### Persistence Backup

```csharp
public class PersistentInMemoryStore<T> : InMemoryStore<T> where T : class
{
    private readonly IStore<T> _backupStore;
    
    public async Task BackupAsync()
    {
        var snapshot = await GetAllAsync();
        await _backupStore.SaveBatchAsync(snapshot);
    }
    
    public async Task RestoreAsync()
    {
        var data = await _backupStore.GetAllAsync();
        await Clear();
        await SaveBatchAsync(data);
    }
}
```

## Troubleshooting

### Common Issues

1. **OutOfMemoryException**
   - Solution: Implement eviction policy or use paging

2. **Concurrent Modification Exception**
   - Solution: Use transaction support for atomic operations

3. **Performance Degradation**
   - Solution: Monitor item count and implement cleanup

### Debugging Tips

```csharp
// Enable detailed logging
services.AddLogging(builder =>
{
    builder.AddDebug();
    builder.SetMinimumLevel(LogLevel.Trace);
});

// Add diagnostics
store.EnableDiagnostics = true;
store.DiagnosticEvent += (sender, args) =>
{
    logger.LogDebug($"Store operation: {args.Operation} on key: {args.Key}");
};
```