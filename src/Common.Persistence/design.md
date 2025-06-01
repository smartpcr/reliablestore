# Common.Persistence Design

## Overview

The Common.Persistence library provides a flexible, transaction-aware persistence layer for .NET applications. It follows a provider pattern where different storage implementations are separated into dedicated projects, allowing for clean separation of concerns and modular deployment.

## Design Principles

### 1. Transaction Agnostic
- **No Transaction Awareness**: Persistence layer has no knowledge of transactions
- **Stateless Operations**: Each operation is independent and atomic at the storage level
- **Transaction Integration**: Higher-level transaction management (Common.Tx) coordinates multiple operations
- **Resource Interface**: Implements `ITransactionalResource` for transaction participation without internal transaction logic

### 2. Storage Abstraction
- **Multiple Backend Support**: Cache, cluster databases, relational databases, file systems, in-memory stores
- **Consistent Interface**: Uniform API across all storage implementations
- **Provider Pattern**: Pluggable storage providers with runtime selection
- **Configuration-Driven**: Storage selection and configuration through dependency injection

### 3. Separation of Concerns
- Core abstractions separated from implementations
- Each provider in its own project for independent deployment
- Clear interfaces between layers

### 4. Transaction Support
- Full integration with Common.Tx transaction management
- Two-phase commit protocol support
- Savepoint functionality where applicable

### 5. Performance Optimization
- Provider-specific optimizations (caching, batching, etc.)
- Lazy loading and deferred execution patterns
- Thread-safe implementations

### 6. Extensibility
- Plugin architecture through IStorageProvider<T>
- Easy to add new providers without modifying core library
- Configuration-driven provider selection

## Architecture

### Core Components

The main `Common.Persistence` project contains only the essential abstractions and core functionality:

#### Abstractions
- **`IStorageProvider<T>`** - Core interface for all storage providers
- **`ISerializer<T>`** - Interface for entity serialization/deserialization

#### Core Classes
- **`TransactionalRepository<T>`** - Bridges `IStorageProvider<T>` with Common.Tx `IRepository<T>` interface
- **Entity Definitions** - Product, Order, Customer, Payment, Shipment

### Provider Projects

Each storage provider implementation is in a separate project following the pattern `Common.Persistence.Providers.{ProviderType}`:

#### Common.Persistence.Providers.FileStore
- **`FileStore<T>`** - File-based storage with JSON serialization
- **Features:**
  - Direct file persistence with JSON format
  - In-memory caching with file synchronization
  - Thread-safe operations using SemaphoreSlim
  - Transaction support through ITransactionalResource

#### Common.Persistence.Providers.FileSystem
- **`FileSystemProvider<T>`** - Advanced file system provider
- **`FileSystemOptions`** - Configuration options
- **Features:**
  - Lazy-loaded caching strategy
  - Atomic writes using temp file + rename pattern
  - Expression-based querying support
  - Configurable backup and retention policies

#### Common.Persistence.Providers.InMemory
- **`InMemoryProvider<T>`** - High-performance in-memory storage
- **`InMemoryOptions`** - Configuration options
- **`CacheEntry<T>`** - Cache entry with TTL and access tracking
- **Features:**
  - ConcurrentDictionary-based storage
  - LRU eviction strategy
  - Configurable TTL and cache size limits
  - Background eviction timer

### Project Dependencies

```
Common.Persistence (Core)
├── Common.Tx (transaction support)
├── Microsoft.Extensions.Logging.Abstractions
└── Microsoft.Extensions.DependencyInjection.Abstractions

Common.Persistence.Providers.FileStore
├── Common.Tx (for ITransactionalResource)
├── Newtonsoft.Json (serialization)
└── Microsoft.Extensions.Logging.Abstractions

Common.Persistence.Providers.FileSystem
├── Common.Persistence (for IStorageProvider<T>)
└── Microsoft.Extensions.Logging.Abstractions

Common.Persistence.Providers.InMemory
├── Common.Persistence (for IStorageProvider<T>)
└── Microsoft.Extensions.Logging.Abstractions
```

## Usage Patterns

### Basic Repository Usage
```csharp
// Using with transaction support
using var transaction = transactionFactory.CreateTransaction();
var repository = new TransactionalRepository<Product>(storageProvider);
transaction.EnlistResource(repository);

await repository.SaveAsync("product1", new Product { Id = "product1", Name = "Widget" });
await transaction.CommitAsync();
```

### Provider Selection
```csharp
// File-based storage
var fileStore = new FileStore<Product>("data/products.json", logger);

// In-memory storage with options
var inMemoryOptions = new InMemoryOptions 
{
    MaxCacheSize = 5000,
    DefaultTTL = TimeSpan.FromHours(1)
};
var inMemoryProvider = new InMemoryProvider<Product>(inMemoryOptions, logger);

// File system provider with configuration
var fileSystemOptions = new FileSystemOptions 
{
    FilePath = "data/products.json",
    EnableBackups = true
};
var fileSystemProvider = new FileSystemProvider<Product>(fileSystemOptions, serializer, logger);
```

## Configuration

### FileStore Configuration
- **FilePath** - Path to the JSON storage file
- **Logger** - ILogger instance for diagnostic output

### InMemory Configuration
- **DefaultTTL** - Time-to-live for cache entries
- **MaxCacheSize** - Maximum number of entries (0 = unlimited)
- **EnableEviction** - Enable background eviction timer
- **EvictionInterval** - Frequency of eviction runs
- **EvictionStrategy** - Eviction algorithm (currently LRU)

### FileSystem Configuration
- **FilePath** - Path to the storage file
- **BackupDirectory** - Directory for backup files
- **BackupRetentionDays** - Number of days to retain backups
- **EnableBackups** - Enable automatic backup creation

## Implementation Details

### Thread Safety

All providers are designed to be thread-safe:
- **FileStore** - Uses SemaphoreSlim and lock statements
- **InMemory** - Uses ConcurrentDictionary and atomic operations
- **FileSystem** - Uses SemaphoreSlim for file operations and locks for cache access

### Testing Strategy

Each provider should include comprehensive tests covering:
- Basic CRUD operations
- Concurrent access scenarios
- Transaction integration
- Error handling and recovery
- Performance characteristics

## Future Enhancements

### Planned Provider Types
- **Database Providers** - SQL Server, PostgreSQL, MySQL
- **NoSQL Providers** - MongoDB, Redis, Cosmos DB
- **Cloud Providers** - Azure Blob Storage, AWS S3
- **Hybrid Providers** - Multi-tier caching strategies

### Advanced Features
- **Encryption Support** - At-rest and in-transit encryption
- **Compression** - Configurable compression for storage efficiency
- **Sharding** - Horizontal scaling support
- **Replication** - Multi-master and read replica support
- **Health Monitoring** - Provider health checks and metrics
- **Circuit Breaker** - Fault tolerance patterns