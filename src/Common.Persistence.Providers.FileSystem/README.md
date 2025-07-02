# FileSystem Persistence Provider

A high-performance file-based persistence provider that stores each entity as a separate JSON file on disk. This implementation follows the pattern from CsvFileCache for better performance and concurrency.

## Features

- **Individual File Storage**: Each entity is stored in its own JSON file for better isolation and concurrency
- **Atomic Operations**: Uses temporary files and atomic rename operations to ensure data consistency
- **Optimized for Concurrent Access**: Per-file locking allows multiple entities to be accessed simultaneously
- **Automatic Directory Organization**: Uses subdirectories based on key prefixes to improve file system performance
- **Retry Logic**: Built-in retry mechanism for handling transient file system errors
- **Thread-Safe**: Full support for concurrent read/write operations
- **Transactional Support**: Full ACID transaction support through the Common.Tx framework

## Configuration

### Basic Setup

```json
{
  "Providers": {
    "FileSystem": {
      "Name": "FileSystem",
      "FilePath": "data/entities/dummy.json",
      "UseSubdirectories": true,
      "MaxConcurrentFiles": 16,
      "MaxRetries": 3,
      "RetryDelayMs": 100,
      "Enabled": true
    }
  }
}
```

### Settings Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `FilePath` | string | "data/entities/dummy.json" | Base directory for storing entity files (filename ignored) |
| `UseSubdirectories` | bool | true | Create subdirectories based on key prefix for better performance |
| `MaxConcurrentFiles` | int? | ProcessorCount * 2 | Maximum concurrent file operations |
| `MaxRetries` | int | 3 | Number of retry attempts for file operations |
| `RetryDelayMs` | int | 100 | Delay between retries in milliseconds |
| `Enabled` | bool | true | Enable/disable the provider |

## File Organization

When `UseSubdirectories` is enabled, files are organized as follows:

```
data/entities/
├── aa/
│   ├── aa-product-001.json
│   └── aa-product-002.json
├── ab/
│   └── ab-product-001.json
└── pr/
    ├── product-001.json
    └── product-002.json
```

Each entity is stored in a subdirectory based on the first two characters of its sanitized key.

## Usage Examples

### Basic CRUD Operations

```csharp
// Configure services
services.AddConfiguration(config =>
{
    config["Providers:FileSystem:FilePath"] = "data/products/dummy.json";
    config["Providers:FileSystem:UseSubdirectories"] = "true";
});
services.AddPersistence();

// Use the provider
var factory = serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
using var provider = factory.Create<Product>("FileSystem");

// Create/Update
var product = new Product { Id = "prod-123", Name = "Example Product", Price = 99.99m };
await provider.SaveAsync(product.Id, product);

// Read
var retrieved = await provider.GetAsync("prod-123");

// Check existence
var exists = await provider.ExistsAsync("prod-123");

// Delete
await provider.DeleteAsync("prod-123");

// Query all
var allProducts = await provider.GetAllAsync();
var expensiveProducts = await provider.GetAllAsync(p => p.Price > 100);

// Count
var totalCount = await provider.CountAsync();
var expensiveCount = await provider.CountAsync(p => p.Price > 100);
```

### Batch Operations

```csharp
// Save multiple entities
var products = new[]
{
    new KeyValuePair<string, Product>("prod1", new Product { Id = "prod1", Name = "Widget A" }),
    new KeyValuePair<string, Product>("prod2", new Product { Id = "prod2", Name = "Widget B" })
};

await provider.SaveManyAsync(products);

// Read multiple entities
var keys = new[] { "prod1", "prod2", "prod3" };
var retrievedProducts = await provider.GetManyAsync(keys);
```

### Transactional Operations

```csharp
using var transaction = transactionFactory.CreateTransaction();
transaction.EnlistResource(provider);

try
{
    await provider.SaveAsync("prod1", product1);
    await provider.SaveAsync("prod2", product2);
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

## Performance Characteristics

- **Write Performance**: Excellent - each entity write is independent
- **Read Performance**: Excellent - direct file access with no parsing overhead
- **Concurrent Access**: Excellent - per-file locking allows high concurrency
- **Memory Usage**: Low - no in-memory cache, data is read on demand
- **Scalability**: Good for moderate datasets (thousands to tens of thousands of entities)

### Performance Tips

1. **Enable Subdirectories**: Always use `UseSubdirectories=true` for datasets with more than a few hundred entities
2. **SSD Storage**: Use fast SSDs for best performance
3. **Increase Concurrent Files**: For high-concurrency scenarios, increase `MaxConcurrentFiles`
4. **Batch Operations**: Use `SaveManyAsync` for bulk inserts

## Best Practices

1. **Key Design**: Design keys to distribute well across subdirectories
   - Good: UUIDs, sequential IDs with prefixes
   - Avoid: All keys starting with same characters

2. **Error Handling**: Always handle I/O exceptions
   ```csharp
   try
   {
       await provider.SaveAsync(key, entity);
   }
   catch (IOException ex)
   {
       // Handle file system errors
   }
   ```

3. **Cleanup**: Dispose providers when done
   ```csharp
   using var provider = factory.Create<Product>("FileSystem");
   // Use provider
   ```

4. **Backup Strategy**: The file-based nature makes backup straightforward
   - Simply copy the entire data directory
   - Use file system snapshots for consistency

## Limitations

- **Path Length**: Windows has a 260-character path limit by default
- **Special Characters**: Some characters in keys may be sanitized
- **Large Datasets**: Not optimal for millions of entities due to file system overhead
- **Query Performance**: `GetAllAsync` with predicates requires reading all files
- **Atomic Batch Operations**: Batch operations are not atomic across multiple files

## Migration from Old Single-File Provider

The previous implementation stored all entities in a single JSON file. To migrate:

1. Export data from old provider
2. Import into new provider
3. Benefits of migration:
   - Better concurrency (no global lock)
   - Better performance for large datasets
   - More resilient (corruption affects single entity, not all)

Example migration code:
```csharp
// Read from old provider
var oldData = await oldProvider.GetAllAsync();

// Write to new provider
foreach (var entity in oldData)
{
    await newProvider.SaveAsync(entity.Id, entity);
}
```

## Error Handling

Common exceptions and their meanings:

- `IOException`: File in use, disk full, or other I/O error
- `UnauthorizedAccessException`: Insufficient permissions
- `DirectoryNotFoundException`: Base directory doesn't exist
- `ArgumentException`: Invalid characters in key

## Thread Safety

The provider is fully thread-safe:
- Multiple threads can read different entities simultaneously
- Multiple threads can write different entities simultaneously
- Per-file locking prevents conflicts
- Atomic file operations ensure consistency

## Monitoring and Diagnostics

Enable debug logging to see detailed operations:

```csharp
services.AddLogging(builder =>
{
    builder.SetMinimumLevel(LogLevel.Debug);
});
```

Key metrics to monitor:
- File operation count
- Retry occurrences
- Operation latency
- Disk space usage