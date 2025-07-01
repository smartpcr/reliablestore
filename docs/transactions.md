# Transaction Support in ReliableStore

This document explains how the Common.Tx library provides transaction support for storage providers that don't have native transaction capabilities, enabling ACID properties across distributed operations.

## Overview

The Common.Tx library implements a comprehensive transaction abstraction layer that adds ACID (Atomicity, Consistency, Isolation, Durability) properties to any storage provider. It uses a **two-phase commit protocol** with support for savepoints, rollback mechanisms, and compensation patterns.

## Architecture

### Core Components

#### 1. TransactionCoordinator

The central transaction manager that orchestrates distributed transactions:

- Implements the `ITransaction` interface
- Manages transaction lifecycle states: Active → Preparing → Prepared → Committing → Committed/RolledBack
- Coordinates two-phase commit across multiple resources
- Handles transaction timeouts with automatic rollback
- Thread-safe for concurrent operations

#### 2. ITransactionalResource Interface

All transactional resources must implement this interface:

```csharp
public interface ITransactionalResource
{
    Task<bool> PrepareAsync(CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
    
    // Savepoint support
    Task<ISavepoint> CreateSavepointAsync(string name);
    Task RollbackToSavepointAsync(ISavepoint savepoint);
}
```

#### 3. TransactionalRepository<T>

A generic wrapper that adds transaction support to any repository:

```csharp
public class TransactionalRepository<T> : ITransactionalRepository<T>, ITransactionalResource
{
    private readonly IRepository<T> underlyingRepository;
    private readonly ConcurrentDictionary<string, TransactionOperation<T>> pendingOperations;
    
    // Stages operations in memory until commit
}
```

#### 4. TransactionalResource<T>

A more sophisticated wrapper for persistence providers that integrates with `ICrudStorageProvider<T>`:

- Maintains staged operations and deleted keys separately
- Supports entity validation during prepare phase
- Provides savepoint snapshots for partial rollback

## How It Works

### Operation Staging

Instead of immediately persisting changes, all operations are staged in memory:

```csharp
// Operations are tracked but not applied to storage
private readonly Dictionary<string, TransactionOperation<T>> stagedOperations;
private readonly HashSet<string> deletedKeys;

public async Task SaveAsync(string key, T entity, CancellationToken cancellationToken)
{
    // Stage the operation instead of persisting
    this.stagedOperations[key] = new TransactionOperation<T>
    {
        Type = OperationType.Update,
        Key = key,
        NewValue = entity,
        OriginalValue = await GetOriginalValue(key)
    };
}
```

### Two-Phase Commit Protocol

#### Phase 1: Prepare

All enlisted resources validate their staged operations:

```csharp
public async Task<bool> PrepareAsync(CancellationToken cancellationToken)
{
    // Validate all staged operations
    foreach (var operation in this.stagedOperations.Values)
    {
        if (!await ValidateOperation(operation))
        {
            return false;
        }
    }
    
    // Check for conflicts
    if (await DetectConflicts())
    {
        return false;
    }
    
    return true;
}
```

Validation includes:
- Checking entity constraints
- Verifying entities exist for updates/deletes
- Detecting optimistic concurrency conflicts
- Ensuring storage capacity

#### Phase 2: Commit

If all resources prepare successfully, changes are applied:

```csharp
public async Task CommitAsync(CancellationToken cancellationToken)
{
    // Apply all staged operations to underlying storage
    var saveOperations = new List<KeyValuePair<string, T>>();
    
    foreach (var operation in this.stagedOperations.Values)
    {
        switch (operation.Type)
        {
            case OperationType.Insert:
            case OperationType.Update:
                saveOperations.Add(new KeyValuePair<string, T>(operation.Key, operation.NewValue));
                break;
        }
    }
    
    // Batch save for efficiency
    if (saveOperations.Any())
    {
        await this.underlyingProvider.SaveManyAsync(saveOperations, cancellationToken);
    }
    
    // Delete operations
    foreach (var key in this.deletedKeys)
    {
        await this.underlyingProvider.DeleteAsync(key, cancellationToken);
    }
    
    // Clear staged operations
    this.stagedOperations.Clear();
    this.deletedKeys.Clear();
}
```

### Transaction Isolation

The wrapper provides read isolation by serving reads from staged data:

```csharp
public async Task<T> GetAsync(string key, CancellationToken cancellationToken)
{
    // Check staged operations first
    if (this.stagedOperations.TryGetValue(key, out var operation))
    {
        switch (operation.Type)
        {
            case OperationType.Insert:
            case OperationType.Update:
                return operation.NewValue;  // Return staged value
            case OperationType.Delete:
                return null;  // Entity is deleted in this transaction
        }
    }
    
    // Check if key is deleted in this transaction
    if (this.deletedKeys.Contains(key))
    {
        return null;
    }
    
    // Fall back to underlying storage
    return await this.underlyingProvider.GetAsync(key, cancellationToken);
}
```

### Rollback Mechanism

Rollback is simple since nothing was persisted:

```csharp
public async Task RollbackAsync(CancellationToken cancellationToken)
{
    // Simply discard all staged operations
    this.stagedOperations.Clear();
    this.deletedKeys.Clear();
    
    // No cleanup needed in underlying storage
    await Task.CompletedTask;
}
```

### Savepoint Support

Savepoints enable partial rollback within a transaction:

```csharp
public async Task<ISavepoint> CreateSavepointAsync(string name)
{
    return new TransactionSavepoint<T>
    {
        Name = name,
        StagedOperations = new Dictionary<string, TransactionOperation<T>>(this.stagedOperations),
        DeletedKeys = new HashSet<string>(this.deletedKeys)
    };
}

public async Task RollbackToSavepointAsync(ISavepoint savepoint)
{
    var snapshot = (TransactionSavepoint<T>)savepoint;
    
    // Restore state from savepoint
    this.stagedOperations.Clear();
    foreach (var kvp in snapshot.StagedOperations)
    {
        this.stagedOperations[kvp.Key] = kvp.Value;
    }
    
    this.deletedKeys.Clear();
    this.deletedKeys.UnionWith(snapshot.DeletedKeys);
}
```

## Usage Guide

### Basic Transaction Usage

```csharp
public class OrderService
{
    private readonly ITransactionFactory transactionFactory;
    private readonly ICrudStorageProvider<Order> orderStore;
    private readonly ICrudStorageProvider<Payment> paymentStore;
    
    public async Task ProcessOrderAsync(Order order, Payment payment)
    {
        // Create a new transaction
        using var transaction = transactionFactory.CreateTransaction();
        
        try
        {
            // Operations are automatically enlisted in the transaction
            await orderStore.SaveAsync(order.Id, order);
            await paymentStore.SaveAsync(payment.Id, payment);
            
            // Two-phase commit happens here
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            // Automatic rollback on dispose if not committed
            logger.LogError(ex, "Transaction failed");
            throw;
        }
    }
}
```

### Manual Resource Enlistment

```csharp
// Create transaction
using var transaction = transactionFactory.CreateTransaction();

// Create transactional wrapper
var transactionalResource = new TransactionalResource<Customer>(
    customer,
    c => c.Id,
    customerStore  // Non-transactional provider
);

// Manually enlist resource
transaction.EnlistResource(transactionalResource);

// Perform operations
await customerStore.SaveAsync(customer.Id, customer);

// Commit
await transaction.CommitAsync();
```

### Using Savepoints

```csharp
using var transaction = transactionFactory.CreateTransaction();

// Initial operations
await orderStore.SaveAsync(order1.Id, order1);

// Create savepoint
var savepoint = await transaction.CreateSavepointAsync("BeforeRiskyOperation");

try
{
    // Risky operations
    await orderStore.SaveAsync(order2.Id, order2);
    await ValidateComplexBusinessRules();
}
catch
{
    // Rollback to savepoint, keeping order1 changes
    await transaction.RollbackToSavepointAsync(savepoint);
}

// Commit remaining operations
await transaction.CommitAsync();
```

### Transaction with Multiple Providers

```csharp
public async Task DistributedOperationAsync()
{
    using var transaction = transactionFactory.CreateTransaction();
    
    // Mix of providers without native transaction support
    var fileStore = factory.Create<Order>("FileSystemOrders");
    var registryStore = factory.Create<Customer>("ClusterRegistryCustomers");
    var inMemoryStore = factory.Create<Product>("InMemoryProducts");
    
    // All participate in the same transaction
    await fileStore.SaveAsync(order.Id, order);
    await registryStore.SaveAsync(customer.Id, customer);
    await inMemoryStore.SaveAsync(product.Id, product);
    
    // Atomic commit across all stores
    await transaction.CommitAsync();
}
```

## Transaction Flow

```
1. Begin Transaction
   ↓
2. Execute Operations (staged in memory)
   ↓
3. Commit Requested
   ↓
4. Phase 1: Prepare All Resources
   ├─ Validate operations
   ├─ Check constraints
   └─ All must succeed
   ↓
5. Phase 2: Commit All Resources
   ├─ Apply staged operations
   └─ Persist to storage
   ↓
6. Transaction Complete
```

## Benefits

1. **Atomicity**: All-or-nothing semantics across multiple resources
2. **Consistency**: Validation during prepare phase ensures data integrity
3. **Isolation**: Transaction-local view prevents dirty reads
4. **Durability**: Changes only persisted after successful commit
5. **Provider Agnostic**: Works with any storage provider
6. **Performance**: Batch operations and parallel processing

## Best Practices

### 1. Keep Transactions Short

```csharp
// Good: Focused transaction
using var transaction = transactionFactory.CreateTransaction();
await orderStore.SaveAsync(order.Id, order);
await transaction.CommitAsync();

// Avoid: Long-running transactions
using var transaction = transactionFactory.CreateTransaction();
var orders = await GetLargeOrderList();  // Don't do this inside transaction
foreach (var order in orders) { ... }
```

### 2. Handle Failures Gracefully

```csharp
try
{
    using var transaction = transactionFactory.CreateTransaction();
    await PerformOperations();
    await transaction.CommitAsync();
}
catch (TransactionAbortedException ex)
{
    // Transaction was aborted, handle appropriately
    logger.LogWarning(ex, "Transaction aborted");
}
catch (TransactionTimeoutException ex)
{
    // Transaction timed out
    logger.LogError(ex, "Transaction timeout");
}
```

### 3. Use Appropriate Timeout Values

```csharp
var options = new TransactionOptions
{
    Timeout = TimeSpan.FromSeconds(30)  // Adjust based on operation complexity
};

using var transaction = transactionFactory.CreateTransaction(options);
```

### 4. Avoid Nested Transactions

The current implementation doesn't support nested transactions. Use savepoints instead for partial rollback scenarios.

### 5. Test Transaction Scenarios

```csharp
[Fact]
public async Task Transaction_ShouldRollbackOnFailure()
{
    // Arrange
    var order = new Order { Id = "123" };
    
    // Act & Assert
    using var transaction = transactionFactory.CreateTransaction();
    await orderStore.SaveAsync(order.Id, order);
    
    // Force failure
    transaction.Dispose();  // Without commit
    
    // Verify rollback
    var result = await orderStore.GetAsync(order.Id);
    Assert.Null(result);
}
```

## Limitations

1. **No Distributed Transaction Coordinator**: Relies on in-process coordination
2. **No Nested Transactions**: Use savepoints for hierarchical transaction needs
3. **Memory Usage**: Staged operations consume memory until commit
4. **No Cross-Process Transactions**: Transactions are scoped to a single process

## Summary

The Common.Tx library provides robust transaction support for storage providers that lack native transaction capabilities. By implementing a two-phase commit protocol with operation staging, it enables ACID properties across heterogeneous data stores, making it possible to maintain consistency in distributed microservice architectures.