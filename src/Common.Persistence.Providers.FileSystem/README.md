# FileSystem Persistence Provider

## Overview

The FileSystem persistence provider is a file-based storage implementation for the ReliableStore persistence layer. It provides durable, transactional storage using the local file system with JSON serialization.

## Features

- **File-based Storage**: Stores entities as JSON files on the local file system
- **Thread-safe Operations**: Implements concurrent access control using reader/writer locks
- **Transactional Support**: Full ACID transaction support through the Common.Tx framework
- **Type-safe Storage**: Generic implementation supports any entity type
- **Automatic Directory Creation**: Creates storage directories as needed
- **JSON Serialization**: Human-readable storage format for easy debugging

## Configuration

### Basic Setup

```csharp
var settings = new FileSystemStoreSettings
{
    BasePath = "data",           // Base directory for all data files
    SubFolder = "entities",      // Optional subfolder for organization
    UsePrettyJson = true         // Format JSON for readability
};

var store = new FileStore<Product>("products", settings);
```

### Settings Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `BasePath` | string | "data" | Root directory for storage |
| `SubFolder` | string | null | Optional subdirectory |
| `UsePrettyJson` | bool | false | Pretty-print JSON files |
| `FileExtension` | string | ".json" | File extension for entity files |
| `BackupEnabled` | bool | false | Enable automatic backups |
| `BackupRetentionDays` | int | 7 | Days to retain backups |

## Usage Examples

### Basic CRUD Operations

```csharp
// Create
var product = new Product { Id = "123", Name = "Widget", Price = 9.99m };
await store.SaveAsync(product.Id, product);

// Read
var retrieved = await store.GetAsync("123");

// Update
retrieved.Price = 12.99m;
await store.SaveAsync(retrieved.Id, retrieved);

// Delete
await store.DeleteAsync("123");

// Query all
var allProducts = await store.GetAllAsync();
```

### Transactional Operations

```csharp
using var transaction = transactionFactory.CreateTransaction();
transaction.EnlistResource(store);

try
{
    await store.SaveAsync("prod1", product1);
    await store.SaveAsync("prod2", product2);
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

### Batch Operations

```csharp
// Save multiple entities
var products = new Dictionary<string, Product>
{
    ["prod1"] = new Product { Id = "prod1", Name = "Widget A" },
    ["prod2"] = new Product { Id = "prod2", Name = "Widget B" }
};

await store.SaveBatchAsync(products);

// Delete multiple entities
await store.DeleteBatchAsync(new[] { "prod1", "prod2" });
```

## File Structure

The provider creates the following directory structure:

```
data/
├── products/
│   ├── prod1.json
│   ├── prod2.json
│   └── .backup/          # If backups enabled
│       ├── prod1_20240101_120000.json
│       └── prod2_20240101_120000.json
├── orders/
│   ├── order1.json
│   └── order2.json
└── customers/
    ├── cust1.json
    └── cust2.json
```

## Performance Characteristics

- **Read Performance**: O(1) for single entity, O(n) for GetAll
- **Write Performance**: O(1) with file system I/O overhead
- **Memory Usage**: Low - only active entities in memory
- **Scalability**: Limited by file system capabilities
- **Concurrency**: Thread-safe with reader/writer locks

## Best Practices

1. **Directory Organization**: Use meaningful base paths and subfolders
2. **Backup Strategy**: Enable backups for critical data
3. **File Naming**: Use consistent, URL-safe entity IDs
4. **Error Handling**: Always wrap operations in try-catch blocks
5. **Transaction Scope**: Keep transactions short-lived

## Limitations

- Not suitable for high-frequency writes (file system overhead)
- No built-in query capabilities beyond GetAll
- Limited by file system constraints (path length, special characters)
- No built-in replication or clustering support
- Performance degrades with large numbers of files in a directory

## Error Handling

Common exceptions:

- `IOException`: File system access issues
- `UnauthorizedAccessException`: Permission problems
- `DirectoryNotFoundException`: Missing base directory
- `JsonSerializationException`: Entity serialization failures

## Migration Guide

### From InMemory Provider

```csharp
// Before
var store = new InMemoryStore<Product>();

// After
var settings = new FileSystemStoreSettings { BasePath = "data" };
var store = new FileStore<Product>("products", settings);
```

### From Database Provider

1. Export existing data to JSON format
2. Place files in appropriate directory structure
3. Update store initialization code
4. Test thoroughly with sample data

## Security Considerations

- File system permissions control access
- No built-in encryption (use OS-level encryption if needed)
- Sensitive data should use additional protection
- Regular backups recommended for data recovery

## Monitoring and Diagnostics

Enable logging to track operations:

```csharp
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});
```

Key metrics to monitor:
- File I/O operations per second
- Average operation latency
- Disk space usage
- Failed operation count