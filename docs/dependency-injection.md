# Dependency Injection in ReliableStore

This document explains how dependency injection (DI) is implemented in the ReliableStore solution, including configuration management, provider registration, and transaction support.

## Overview

ReliableStore uses a flexible dependency injection architecture that supports both Unity Container and Microsoft.Extensions.DependencyInjection. The system is designed to be provider-agnostic while maintaining consistency across different DI containers.

## Core Components

### 1. Dual Container Support

The solution abstracts DI container differences through the `DIContainerWrapper` class:

```csharp
public class DIContainerWrapper
{
    private readonly IUnityContainer? unityContainer;
    private readonly IServiceProvider? serviceProvider;
    
    // Supports both Unity and Microsoft DI containers
}
```

This allows the codebase to work with either container type without modification.

### 2. Extension Methods for Service Registration

The solution provides two main extension methods for service registration:

#### Persistence Services Registration

```csharp
// In Common.Persistence.Factory.DependencyInjection
public static IServiceCollection AddPersistence(this IServiceCollection services)
{
    services.AddSingleton<IConfigReader, JsonConfigReader>();
    services.AddSingleton<ICrudStorageProviderFactory, CrudStorageProviderFactory>();
    services.AddSingleton<IIndexingProviderFactory, IndexingProviderFactory>();
    services.AddSingleton<IArchivalProviderFactory, ArchivalProviderFactory>();
    services.AddSingleton<IPurgeProviderFactory, PurgeProviderFactory>();
    services.AddSingleton<IBackupProviderFactory, BackupProviderFactory>();
    services.AddSingleton<IMigrationProviderFactory, MigrationProviderFactory>();
    services.AddSingleton<ISerializerFactory, SerializerFactory>();
    
    return services;
}
```

#### Transaction Support Registration

```csharp
// In Common.Tx.TransactionExtensions
public static IServiceCollection AddTransactionSupport(this IServiceCollection services)
{
    services.AddSingleton<ITransactionFactory, TransactionFactory>();
    services.AddSingleton<ITransactionalRepositoryFactory, TransactionalRepositoryFactory>();
    
    return services;
}
```

## Configuration System

### Configuration Structure

Settings are loaded from `appsettings.json` using the following structure:

```json
{
  "Providers": {
    "Product": {
      "Capabilities": "Crud",
      "AssemblyName": "CRP.Common.Persistence.Providers.FileSystem",
      "TypeName": "Common.Persistence.Providers.FileSystem.FileSystemStore`1",
      "FilePath": "data/products.json"
    },
    "Order": {
      "Capabilities": "Crud",
      "AssemblyName": "CRP.Common.Persistence.Providers.ClusterRegistry",
      "TypeName": "Common.Persistence.Providers.ClusterRegistry.ClusterRegistryProvider`1",
      "RegistryPath": "Software\\CRP\\ReliableStore\\Orders",
      "MaxValueSizeKB": 64
    }
  },
  "Serializers": {
    "Default": {
      "AssemblyName": "CRP.Common.Persistence",
      "TypeName": "Common.Persistence.Serializers.JsonEntitySerializer`1"
    }
  }
}
```

### Configuration Reader

The `IConfigReader` interface provides methods to read provider settings:

```csharp
public interface IConfigReader
{
    T? GetSettings<T>(string sectionName) where T : class;
    CrudStorageProviderSettings? GetCrudStorageProviderSettings(string name);
    // Other provider settings methods...
}
```

The `JsonConfigReader` implementation loads settings from JSON configuration files.

## Provider Factory Pattern

### Factory Implementation

Providers are created dynamically using factories that follow this pattern:

```csharp
public class CrudStorageProviderFactory : ICrudStorageProviderFactory
{
    public ICrudStorageProvider<T> Create<T>(string name) where T : class
    {
        // 1. Read provider settings from configuration
        var settings = configReader.GetCrudStorageProviderSettings(name);
        
        // 2. Find constructor using reflection
        var constructor = settings.FindConstructor<T>();
        
        // 3. Register and create instance
        return containerWrapper.TryRegisterAndGetRequired<ICrudStorageProvider<T>>(
            name, 
            constructor);
    }
}
```

### Provider Constructor Pattern

All providers follow a standard constructor pattern to ensure compatibility:

```csharp
// Microsoft DI pattern
public FileSystemStore(IServiceProvider serviceProvider, string name)
{
    this.serviceProvider = serviceProvider;
    var configReader = serviceProvider.GetRequiredService<IConfigReader>();
    this.storeSettings = configReader.GetCrudStorageProviderSettings(name);
    // Initialize provider...
}

// Unity pattern
public FileSystemStore(IUnityContainer container, string name)
{
    this.container = container;
    var configReader = container.Resolve<IConfigReader>();
    this.storeSettings = configReader.GetCrudStorageProviderSettings(name);
    // Initialize provider...
}
```

### Settings Classes Hierarchy

```
BaseProviderSettings
├── CrudStorageProviderSettings
│   ├── FileSystemStoreSettings
│   ├── InMemoryStoreSettings
│   ├── EsentStoreSettings
│   └── ClusterRegistryStoreSettings
├── IndexingProviderSettings
├── ArchivalProviderSettings
└── Other provider settings...
```

Each settings class contains:
- `Name`: Provider instance name
- `AssemblyName`: Assembly containing the provider
- `TypeName`: Fully qualified type name
- `Enabled`: Whether the provider is enabled
- Provider-specific settings

## Transaction Support

The transaction system is registered using the extension method:

```csharp
builder.Services.AddTransactionSupport();
```

This registers `ITransactionFactory` and `ITransactionalRepositoryFactory` as singletons. For detailed information about how transactions work, especially for providers without native transaction support, see the [Transaction Documentation](transactions.md).

## Service Registration in Applications

### Typical Service Configuration

```csharp
// In Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add persistence and transaction support
builder.Services.AddPersistence();
builder.Services.AddTransactionSupport();

// Add application services
builder.Services.AddControllers();
builder.Services.AddLogging();

var app = builder.Build();
```

### Controller with DI

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductController : ControllerBase
{
    private readonly ICrudStorageProvider<Product> productStore;
    private readonly ITransactionFactory transactionFactory;
    private readonly ILogger<ProductController> logger;
    
    public ProductController(
        ICrudStorageProviderFactory factory,
        ITransactionFactory transactionFactory,
        ILogger<ProductController> logger)
    {
        // Factory creates provider based on configuration
        this.productStore = factory.Create<Product>(nameof(Product));
        this.transactionFactory = transactionFactory;
        this.logger = logger;
    }
}
```

## Advanced Scenarios

### Multiple Provider Instances

You can configure multiple instances of the same provider type:

```json
{
  "Providers": {
    "PrimaryProducts": {
      "Capabilities": "Crud",
      "AssemblyName": "CRP.Common.Persistence.Providers.FileSystem",
      "TypeName": "Common.Persistence.Providers.FileSystem.FileSystemStore`1",
      "FilePath": "data/products-primary.json"
    },
    "BackupProducts": {
      "Capabilities": "Crud",
      "AssemblyName": "CRP.Common.Persistence.Providers.FileSystem",
      "TypeName": "Common.Persistence.Providers.FileSystem.FileSystemStore`1",
      "FilePath": "data/products-backup.json"
    }
  }
}
```

```csharp
// Use different instances
var primaryStore = factory.Create<Product>("PrimaryProducts");
var backupStore = factory.Create<Product>("BackupProducts");
```

### Custom Provider Registration

To add a new provider:

1. Create provider class implementing appropriate interface
2. Follow the standard constructor pattern
3. Create settings class extending base settings
4. Add configuration to appsettings.json

```csharp
public class CustomProvider<T> : ICrudStorageProvider<T> where T : class
{
    public CustomProvider(IServiceProvider serviceProvider, string name)
    {
        // Implementation
    }
    
    // Implement interface methods...
}
```

### Provider Capabilities

Providers can support different capabilities:
- **Crud**: Basic CRUD operations
- **Indexing**: Query and indexing support
- **Archival**: Long-term storage
- **Purge**: Data cleanup
- **Backup**: Backup operations
- **Migration**: Data migration

## Best Practices

1. **Use Factories**: Always use factories to create providers rather than direct instantiation
2. **Configuration-Driven**: Define provider types and settings in configuration files
3. **Singleton Factories**: Factories should be registered as singletons for performance
4. **Standard Constructors**: Follow the established constructor pattern for new providers
5. **Dispose Pattern**: Implement IDisposable for providers that manage resources
6. **Logging**: Use ILogger for diagnostic information
7. **Cancellation**: Support CancellationToken in async operations

## Troubleshooting

### Common Issues

1. **Provider Not Found**
   - Check AssemblyName and TypeName in configuration
   - Ensure assembly is referenced in the project
   - Verify generic type parameter matches

2. **Settings Not Loading**
   - Verify JSON structure in appsettings.json
   - Check section names match code expectations
   - Ensure IConfigReader is registered

3. **DI Resolution Failures**
   - Confirm all dependencies are registered
   - Check constructor parameters match registered services
   - Verify service lifetimes are compatible

### Debugging Tips

```csharp
// Log provider creation
logger.LogInformation("Creating provider {Name} of type {Type}", 
    name, settings.TypeName);

// Verify configuration
var settings = configReader.GetCrudStorageProviderSettings("Product");
if (settings == null)
{
    throw new InvalidOperationException($"No settings found for provider: Product");
}
```

## Summary

The ReliableStore dependency injection system provides:
- Flexible dual-container support
- Configuration-driven provider selection
- Consistent factory patterns
- Transaction support integration
- Easy extensibility for new providers

This architecture enables loose coupling, testability, and runtime configuration of storage providers while maintaining type safety and performance.