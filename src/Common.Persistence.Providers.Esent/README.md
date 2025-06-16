# ESENT Persistence Provider

This project implements a persistence provider for the ReliableStore system using Microsoft's Extensible Storage Engine (ESENT) database.

## Overview

ESENT (Extensible Storage Engine) is a NoSQL database engine native to Microsoft Windows that provides ACID transactions, indexing, and backup capabilities. It has been part of Windows since Windows 2000 and powers applications like Active Directory, Exchange Server, and Windows Desktop Search.

### Key Features

- **ACID Transactions**: Full transactional support with commit/rollback capabilities
- **High Performance**: Native Windows integration with minimal overhead
- **Zero Configuration**: No separate database server installation required
- **Crash Recovery**: Automatic recovery from unexpected shutdowns
- **Thread Safety**: Built-in concurrency control
- **Compact Storage**: Efficient binary storage format

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

| Property | Default | Description |
|----------|---------|-------------|
| `DatabasePath` | `"data/esent.db"` | Path to the database file |
| `InstanceName` | `"ReliableStore"` | Unique name for the ESENT instance |
| `CacheSizeMB` | `64` | Memory cache size in megabytes |
| `MaxDatabaseSizeMB` | `1024` | Maximum database size in megabytes |
| `EnableVersioning` | `true` | Enable automatic versioning |
| `PageSizeKB` | `8` | Database page size (2, 4, 8, 16, or 32 KB) |

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
- **Fast Reads**: Excellent read performance for indexed data
- **Efficient Storage**: Binary format with good compression
- **Low Latency**: Direct system integration without network overhead
- **Concurrent Access**: Built-in support for multiple readers and writers

### Considerations
- **Windows Only**: Platform dependency limits deployment options
- **Single Machine**: No built-in clustering or replication
- **Write Scaling**: Performance may degrade under very high write loads
- **Database Size**: Performance optimal under 4GB per database

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

## Related Documentation

- [Common.Persistence Overview](../Common.Persistence/README.md)
- [Transaction Management](../Common.Tx/README.md)
- [Microsoft ESENT Documentation](https://docs.microsoft.com/en-us/windows/win32/extensible-storage-engine/)