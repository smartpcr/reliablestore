# IPersistenceProvider<T> Refactoring for Configuration-Driven DI

## Overview

This document outlines the refactoring of the `IPersistenceProvider<T>` system to support configuration-driven instantiation and proper dependency injection (DI) setup.

## Key Improvements

### 1. Configuration-Driven Architecture

#### Before
- Providers were manually instantiated with hardcoded configurations
- No central configuration management
- Limited flexibility for different environments

#### After
- **Comprehensive Configuration System**: `PersistenceConfiguration` class supports all provider types and settings
- **JSON Configuration Support**: Complete integration with `appsettings.json`
- **Environment-Specific Settings**: Different configurations for development, staging, production
- **Provider-Specific Configuration**: Each provider can have its own retry, circuit breaker, and health monitoring settings

```json
{
  "Persistence": {
    "DefaultProvider": "FileSystem",
    "Providers": {
      "FileSystem": {
        "Type": "FileSystem",
        "Enabled": true,
        "Settings": {
          "FilePath": "data/entities.json",
          "EnableBackups": true
        }
      }
    }
  }
}
```

### 2. Factory Pattern Implementation

#### `IPersistenceProviderFactory`
- **Centralized Provider Creation**: Single point for creating all provider instances
- **Configuration Resolution**: Automatically resolves provider configurations
- **Dependency Injection**: Seamless integration with .NET DI container
- **Validation Support**: Built-in provider validation capabilities
- **Async Initialization**: Support for providers requiring async setup

```csharp
// Create providers using factory
var factory = serviceProvider.GetRequiredService<IPersistenceProviderFactory>();
var provider = factory.CreateProvider<ProductEntity>("FileSystem");
var asyncProvider = await factory.CreateProviderAsync<OrderEntity>("SqlServer");
```

### 3. Enhanced Dependency Injection

#### Service Collection Extensions
Multiple registration patterns to support different scenarios:

```csharp
// From configuration file
services.AddPersistence(configuration);

// Programmatic configuration
services.AddPersistence(options => {
    options.DefaultProvider = "InMemory";
    // ... configure providers
});

// Convenience methods
services.AddFileSystemProvider(basePath: "data")
       .AddInMemoryProvider(maxCacheSize: 1000)
       .AddSqlServerProvider(connectionString);

// Entity-specific providers
services.AddPersistenceFor<ProductEntity>("FileSystem");
services.AddPersistenceFor<OrderEntity>(); // Uses default
```

### 4. Provider Auto-Discovery

#### Automatic Registration
- **Assembly Scanning**: Automatically discovers providers in known assemblies
- **Attribute-Based Configuration**: `[ProviderType("TypeName")]` attribute for custom naming
- **Plugin Architecture**: Easy addition of new provider types without core changes

```csharp
[ProviderType("CustomFile")]
public class CustomFileProvider<T> : IPersistenceProvider<T> where T : class, IEntity
{
    // Implementation
}
```

### 5. Advanced Configuration Features

#### Decorator Pattern Support
- **Retry Logic**: Configurable retry with exponential backoff
- **Circuit Breaker**: Automatic failure detection and recovery
- **Health Monitoring**: Built-in health checks and metrics
- **Caching**: Multi-level caching strategies

#### Configuration Inheritance
- **Global Settings**: Default configurations applied to all providers
- **Provider Override**: Provider-specific settings override global defaults
- **Environment Variables**: Support for environment-based configuration

### 6. Provider Capabilities System

#### Enhanced Interface Design
```csharp
public interface IPersistenceProvider<T> where T : IEntity
{
    IList<ProviderCapabilities> GetCapabilities();
    ICrudStorageProvider<T> GetCrudProvider();
    IIndexingProvider<T> GetIndexingProvider();
    IArchivalProvider<T> GetArchivalProvider();
    IPurgeProvider<T> GetPurgeProvider();
    IBackupProvider<T> GetBackupProvider();
    IMigrationProvider<T> GetMigrationProvider();
}
```

#### Capability Flags
```csharp
[Flags]
public enum ProviderCapabilities
{
    Crud = 0,
    Index = 1 << 0,
    Archive = 1 << 1,
    Purge = 1 << 2,
    Backup = 1 << 3,
    Health = 1 << 4,
    Migration = 1 << 5
}
```

## Usage Patterns

### 1. Configuration File Approach
```csharp
var host = Host.CreateDefaultBuilder()
    .ConfigureServices((context, services) =>
    {
        services.AddPersistence(context.Configuration);
        services.AddPersistenceFor<ProductEntity>("FileSystem");
    })
    .Build();
```

### 2. Programmatic Configuration
```csharp
services.AddPersistence(options =>
{
    options.DefaultProvider = "InMemory";
    options.Providers["FileSystem"] = new ProviderConfiguration
    {
        Type = "FileSystem",
        Settings = new Dictionary<string, object>
        {
            ["FilePath"] = "data/products.json"
        }
    };
});
```

### 3. Factory Usage
```csharp
var factory = serviceProvider.GetRequiredService<IPersistenceProviderFactory>();

// Create specific provider
var provider = factory.CreateProvider<ProductEntity>("FileSystem");

// Create with async initialization
var asyncProvider = await factory.CreateProviderAsync<OrderEntity>();

// Validate all providers
var isValid = await factory.ValidateProvidersAsync();
```

### 4. Direct DI Resolution
```csharp
// Register entity-specific provider
services.AddPersistenceFor<ProductEntity>("FileSystem");

// Resolve directly from container
var provider = serviceProvider.GetRequiredService<IPersistenceProvider<ProductEntity>>();
```

## Configuration Examples

### Complete appsettings.json
```json
{
  "Persistence": {
    "DefaultProvider": "FileSystem",
    "Providers": {
      "FileSystem": {
        "Type": "FileSystem",
        "Enabled": true,
        "Settings": {
          "FilePath": "data/entities.json",
          "EnableBackups": true,
          "BackupRetentionDays": 7
        },
        "Retry": {
          "Enabled": true,
          "MaxRetries": 3,
          "InitialDelay": "00:00:00.100"
        }
      },
      "SqlServer": {
        "Type": "SqlServer",
        "Enabled": true,
        "ConnectionString": "Server=localhost;Database=Store;Trusted_Connection=true;",
        "Settings": {
          "CommandTimeout": 30,
          "BulkInsertBatchSize": 1000
        }
      }
    },
    "Retry": {
      "Enabled": true,
      "MaxRetries": 3,
      "Strategy": "ExponentialBackoff"
    },
    "CircuitBreaker": {
      "Enabled": true,
      "FailureThreshold": 5,
      "Timeout": "00:01:00"
    }
  }
}
```

## Benefits

### For Developers
1. **Simplified Configuration**: Single configuration file for all persistence settings
2. **Type Safety**: Strongly-typed configuration classes with validation
3. **IntelliSense Support**: Full IDE support for configuration properties
4. **Testing**: Easy mocking and testing with DI container
5. **Flexibility**: Switch providers without code changes

### For Operations
1. **Environment-Specific Configuration**: Different settings per environment
2. **Runtime Configuration**: Modify settings without recompilation
3. **Monitoring**: Built-in health checks and metrics
4. **Reliability**: Automatic retry and circuit breaker patterns

### For Architecture
1. **Separation of Concerns**: Clear separation between configuration and implementation
2. **Plugin Architecture**: Easy addition of new providers
3. **Decorator Pattern**: Composable cross-cutting concerns
4. **SOLID Principles**: Follows dependency inversion and single responsibility

## Migration Path

### Step 1: Update Configuration
```csharp
// Old approach
var fileStore = new FileStore<Product>("data/products.json", logger);

// New approach
services.AddPersistence(configuration);
services.AddPersistenceFor<Product>("FileSystem");
```

### Step 2: Update Provider Usage
```csharp
// Old approach
var repository = new TransactionalRepository<Product>(fileStore);

// New approach
var provider = serviceProvider.GetRequiredService<IPersistenceProvider<Product>>();
var crudProvider = provider.GetCrudProvider();
```

### Step 3: Add Configuration File
Create `appsettings.json` with provider configurations and update `Program.cs` to use the new DI extensions.

## Future Enhancements

1. **Configuration Validation**: JSON schema validation for configuration files
2. **Hot Reload**: Dynamic configuration updates without restart
3. **Provider Metrics**: Advanced performance monitoring and analytics
4. **Configuration UI**: Web-based configuration management interface
5. **Cloud Integration**: Native support for cloud configuration services

This refactoring provides a robust, scalable, and maintainable foundation for persistence provider management while maintaining backward compatibility and enabling future extensibility.