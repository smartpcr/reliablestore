# SQLite Persistence Provider

## Overview

The SQLite persistence provider enables reliable local storage using SQLite, a self-contained, serverless, zero-configuration database engine. This provider is ideal for applications requiring embedded database capabilities, local caching, or development/testing scenarios with full ACID transaction support.

## Features

- **Zero Configuration**: No server setup or administration required
- **ACID Transactions**: Full support for atomic, consistent, isolated, and durable transactions
- **Cross-Platform**: Works on Windows, Linux, and macOS
- **Embedded Database**: Database runs in-process with your application
- **File-Based Storage**: Single file contains entire database
- **In-Memory Support**: `:memory:` databases for testing and temporary data
- **Thread-Safe**: Multiple threads can safely access the database
- **Schema Management**: Automatic table creation and migration support
- **JSON Storage**: Stores entities as JSON with indexed key access
- **Versioning Support**: Built-in optimistic concurrency control
- **Small Footprint**: Minimal resource usage and deployment size

## Performance Characteristics

SQLite provides excellent performance for local data access:

- **Reads**: Sub-millisecond for key-based lookups
- **Writes**: 1-5ms for typical entity sizes
- **Batch Operations**: Significantly faster with transactions
- **Concurrency**: Read-heavy workloads scale well, write operations are serialized
- **Data Size**: Handles databases up to 281TB (theoretical limit)

### Performance by Operation Type
| Operation | Single Item | Batch (100 items) | Batch (1000 items) |
|-----------|------------|-------------------|-------------------|
| Read | <1ms | 5-10ms | 50-100ms |
| Write | 1-5ms | 10-20ms | 100-200ms |
| Update | 1-5ms | 15-25ms | 150-250ms |
| Delete | 1-3ms | 10-15ms | 100-150ms |

## Prerequisites

### System Requirements
- .NET 9.0 or later
- Any operating system supported by .NET
- Sufficient disk space for database file
- Write permissions to database directory

### NuGet Package
```xml
<PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.0" />
```

## Configuration

### Basic Setup

```csharp
var settings = new SQLiteProviderSettings
{
    DataSource = "reliablestore.db",
    Schema = "myapp"
};

var store = new SQLiteProvider<Product>(settings);
await store.InitializeAsync();
```

### Configuration Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DataSource` | string | `"reliablestore.db"` | Database file path or `:memory:` |
| `Schema` | string | `null` | Table name prefix for multi-tenant scenarios |
| `Mode` | SqliteOpenMode | `ReadWriteCreate` | Database open mode |
| `Cache` | SqliteCacheMode | `Shared` | Connection caching mode |
| `ForeignKeys` | bool | `true` | Enable foreign key constraints |
| `CommandTimeout` | int | `30` | Command timeout in seconds |
| `AutoCreateTables` | bool | `true` | Automatically create tables if missing |
| `ConnectionString` | string | Generated | Override with custom connection string |

## Usage Examples

### Basic Operations

```csharp
// Initialize store
var store = new SQLiteProvider<Customer>(settings);
await store.InitializeAsync();

// Save entity
var customer = new Customer 
{ 
    Id = "CUST-001", 
    Name = "Acme Corp",
    Email = "contact@acme.com"
};
await store.CreateAsync(customer.Id, customer);

// Read entity
var retrieved = await store.GetAsync("CUST-001");

// Update with optimistic concurrency
retrieved.Email = "newcontact@acme.com";
await store.UpdateAsync(retrieved.Id, retrieved, retrieved.ETag);

// Delete
await store.DeleteAsync("CUST-001");

// Query all
var allCustomers = await store.GetAllAsync();
```

### In-Memory Database for Testing

```csharp
// Perfect for unit tests - no file I/O
var testSettings = new SQLiteProviderSettings
{
    DataSource = ":memory:",
    Schema = "test"
};

using var store = new SQLiteProvider<Product>(testSettings);
await store.InitializeAsync();

// Run tests...
// Database is automatically cleaned up when disposed
```

### Batch Operations with Transactions

SQLite excels at batch operations when wrapped in transactions:

```csharp
// Batch insert with transaction
var products = GenerateProducts(1000);

using var connection = store.CreateConnection();
await connection.OpenAsync();
using var transaction = await connection.BeginTransactionAsync();

try
{
    foreach (var product in products)
    {
        await store.CreateAsync(product.Id, product, transaction);
    }
    
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

### Multi-Tenant Support

```csharp
// Separate schemas for different tenants
var tenant1Store = new SQLiteProvider<Order>(new SQLiteProviderSettings
{
    DataSource = "shared.db",
    Schema = "tenant1"
});

var tenant2Store = new SQLiteProvider<Order>(new SQLiteProviderSettings
{
    DataSource = "shared.db",
    Schema = "tenant2"
});

// Tables created as: tenant1_Order, tenant2_Order
```

## Architecture

### Storage Layout

SQLite provider creates one table per entity type with the following schema:

```sql
CREATE TABLE IF NOT EXISTS {Schema}_{EntityType} (
    Key TEXT PRIMARY KEY NOT NULL,
    Data TEXT NOT NULL,
    Version INTEGER NOT NULL DEFAULT 1,
    ETag TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_{Schema}_{EntityType}_UpdatedAt 
    ON {Schema}_{EntityType}(UpdatedAt);
```

### Data Format

Entities are stored as JSON in the `Data` column:

```json
{
  "Id": "PROD-001",
  "Name": "Premium Widget",
  "Price": 99.99,
  "Category": "Widgets",
  "InStock": true
}
```

### Connection Management

The provider uses connection pooling for optimal performance:

```csharp
// Connection string with pooling options
var settings = new SQLiteProviderSettings
{
    DataSource = "app.db",
    Cache = SqliteCacheMode.Shared,  // Enable connection pooling
    ConnectionString = "Data Source=app.db;Mode=ReadWriteCreate;Cache=Shared;Pooling=True"
};
```

## Performance Optimization

### 1. Use WAL Mode for Better Concurrency

```csharp
// Enable Write-Ahead Logging for better concurrent access
public async Task EnableWALMode()
{
    using var connection = store.CreateConnection();
    await connection.OpenAsync();
    
    using var command = connection.CreateCommand();
    command.CommandText = "PRAGMA journal_mode=WAL";
    await command.ExecuteNonQueryAsync();
}
```

### 2. Optimize for Write Performance

```csharp
// Batch writes with optimal settings
var settings = new SQLiteProviderSettings
{
    DataSource = "highperf.db",
    ConnectionString = @"
        Data Source=highperf.db;
        Mode=ReadWriteCreate;
        Cache=Shared;
        Journal Mode=WAL;
        Synchronous=Normal;
        Page Size=4096;
        Temp Store=Memory;
        Mmap Size=30000000000"
};
```

### 3. Index Optimization

```csharp
// Add custom indexes for query patterns
public async Task AddCustomIndexAsync()
{
    using var connection = store.CreateConnection();
    await connection.OpenAsync();
    
    using var command = connection.CreateCommand();
    command.CommandText = @"
        CREATE INDEX IF NOT EXISTS idx_Customer_Email 
        ON Customer(json_extract(Data, '$.Email'))";
    await command.ExecuteNonQueryAsync();
}
```

## Best Practices

### 1. Database File Management
- Place database files on fast local storage (SSD preferred)
- Regular VACUUM to reclaim space and optimize performance
- Use separate database files for different data categories
- Implement regular backup procedures

### 2. Connection Handling
```csharp
// Always dispose connections properly
await using var connection = store.CreateConnection();
await connection.OpenAsync();
// ... use connection
// Automatic disposal ensures connection returns to pool
```

### 3. Transaction Best Practices
```csharp
// Use transactions for multiple operations
public async Task BulkOperationAsync<T>(IEnumerable<T> items) 
    where T : IEntity
{
    using var connection = store.CreateConnection();
    await connection.OpenAsync();
    using var transaction = await connection.BeginTransactionAsync();
    
    try
    {
        foreach (var item in items)
        {
            await store.CreateAsync(item.Id, item, transaction);
        }
        
        await transaction.CommitAsync();
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}
```

### 4. Memory Database Guidelines
- Use `:memory:` for unit tests and temporary data only
- Memory databases are cleared when all connections close
- Keep at least one connection open to preserve data
- Not suitable for production data persistence

## Limitations

### SQLite Limitations
- **Write Concurrency**: Only one writer at a time (readers don't block)
- **Network Access**: Not designed for network file systems
- **Maximum Database Size**: 281TB (practical limit much lower)
- **Maximum Page Size**: 65536 bytes
- **SQL Features**: Subset of SQL standard (no stored procedures)

### Provider Limitations
- **No Full-Text Search**: Use specialized providers for FTS needs
- **Limited Query Support**: Key-based access only (no LINQ)
- **JSON Queries**: Limited to basic JSON extraction
- **No Distributed Transactions**: Local transactions only

## Security Considerations

### Database Encryption

```csharp
// Use SQLCipher for encrypted databases
var settings = new SQLiteProviderSettings
{
    ConnectionString = "Data Source=secure.db;Password=MySecurePassword"
};
```

### Access Control
- Use file system permissions to control database access
- Run application with minimal required privileges
- Store database files outside web root
- Implement application-level access control

### SQL Injection Prevention
The provider uses parameterized queries internally, but be cautious with custom queries:

```csharp
// Safe: Provider handles parameterization
await store.GetAsync(userInput);

// If using custom SQL, always parameterize
command.CommandText = "SELECT * FROM Table WHERE Id = @id";
command.Parameters.AddWithValue("@id", userInput);
```

## Troubleshooting

### Common Issues

1. **Database Locked Errors**
   - Enable WAL mode for better concurrency
   - Reduce transaction duration
   - Check for unclosed connections
   - Verify file system permissions

2. **Performance Issues**
   - Run ANALYZE to update statistics
   - VACUUM to defragment database
   - Check for missing indexes
   - Monitor transaction size

3. **Corruption Recovery**
   ```bash
   # Check database integrity
   sqlite3 mydb.db "PRAGMA integrity_check"
   
   # Attempt recovery
   sqlite3 mydb.db ".recover" | sqlite3 recovered.db
   ```

### Diagnostic Queries

```sql
-- Check database stats
PRAGMA page_count;
PRAGMA page_size;
PRAGMA journal_mode;
PRAGMA synchronous;

-- Analyze table structure
PRAGMA table_info(Customer);

-- Check for locks
PRAGMA lock_status;
```

## Migration Guide

### From InMemory Provider

```csharp
// Simple migration - change settings
var oldSettings = new InMemoryProviderSettings();
var newSettings = new SQLiteProviderSettings 
{ 
    DataSource = "migrated.db" 
};

// Data migration
var inMemoryStore = new InMemoryProvider<Product>(oldSettings);
var sqliteStore = new SQLiteProvider<Product>(newSettings);

await sqliteStore.InitializeAsync();

var allData = await inMemoryStore.GetAllAsync();
foreach (var item in allData)
{
    await sqliteStore.CreateAsync(item.Key, item.Value);
}
```

### From FileSystem Provider

```csharp
public async Task MigrateFromFileSystemAsync(
    FileStore<T> source, 
    SQLiteProvider<T> target) where T : class, IEntity
{
    var items = await source.GetAllAsync();
    
    using var connection = target.CreateConnection();
    await connection.OpenAsync();
    using var transaction = await connection.BeginTransactionAsync();
    
    try
    {
        foreach (var kvp in items)
        {
            await target.CreateAsync(kvp.Key, kvp.Value, transaction);
        }
        
        await transaction.CommitAsync();
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}
```

## Advanced Scenarios

### Custom SQL Queries

```csharp
public async Task<IEnumerable<T>> QueryByPropertyAsync<T>(
    string propertyName, 
    object value) where T : class, IEntity
{
    using var connection = CreateConnection();
    await connection.OpenAsync();
    
    using var command = connection.CreateCommand();
    command.CommandText = $@"
        SELECT Data FROM {typeof(T).Name} 
        WHERE json_extract(Data, '$.{propertyName}') = @value";
    command.Parameters.AddWithValue("@value", value);
    
    var results = new List<T>();
    using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var json = reader.GetString(0);
        results.Add(JsonConvert.DeserializeObject<T>(json));
    }
    
    return results;
}
```

### Database Maintenance

```csharp
public class SQLiteMaintenanceService
{
    private readonly string _connectionString;
    
    public async Task OptimizeDatabaseAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        // Update statistics
        await ExecuteCommandAsync(connection, "ANALYZE");
        
        // Defragment database
        await ExecuteCommandAsync(connection, "VACUUM");
        
        // Optimize for faster queries
        await ExecuteCommandAsync(connection, "PRAGMA optimize");
    }
    
    public async Task<long> GetDatabaseSizeAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA page_count";
        var pageCount = (long)await command.ExecuteScalarAsync();
        
        command.CommandText = "PRAGMA page_size";
        var pageSize = (long)await command.ExecuteScalarAsync();
        
        return pageCount * pageSize;
    }
}
```

## Comparison with Other Providers

| Feature | SQLite | ESENT | FileSystem | InMemory | ClusterRegistry |
|---------|---------|-------|------------|----------|-----------------|
| Performance | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ |
| Persistence | Yes | Yes | Yes | No | Yes |
| Transactions | Full ACID | Full ACID | Basic | Full | Batch only |
| Cross-Platform | Yes | No | Yes | Yes | No |
| Zero Config | Yes | Yes | Yes | Yes | No |
| Query Support | SQL | Indexed | None | LINQ | None |
| Max Database Size | 281TB | 16TB | OS Limit | RAM | 1MB |
| Concurrent Writers | No* | Yes | No | Yes | Yes |
| Setup Complexity | Low | Medium | Low | None | High |
| Network Support | No** | No | Yes | No | Yes |

\* WAL mode allows concurrent readers during writes
\** Not recommended on network file systems

### When to Choose SQLite

✅ **Good fit for:**
- Desktop applications needing local data storage
- Mobile applications (via Xamarin/MAUI)
- Development and testing environments
- Single-user or low-concurrency scenarios
- Applications requiring SQL query capabilities
- Embedded scenarios with limited resources
- Cross-platform applications

❌ **Poor fit for:**
- High-concurrency write workloads
- Network-based storage requirements
- Applications requiring multiple concurrent writers
- Scenarios needing distributed transactions
- Real-time replication requirements

## Related Documentation

- [Common.Persistence Overview](../Common.Persistence/README.md)
- [Persistence Providers Comparison](../docs/persistence-providers.md)
- [SQLite Official Documentation](https://www.sqlite.org/docs.html)
- [Transaction Management](../Common.Tx/README.md)
- [Microsoft.Data.Sqlite Documentation](https://docs.microsoft.com/en-us/dotnet/standard/data/sqlite/)