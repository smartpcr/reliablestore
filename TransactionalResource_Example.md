# TransactionalResource<T> Usage Example

The `TransactionalResource<T>` class provides a generic wrapper that makes any storage provider transactional by staging operations until commit.

## Basic Usage

```csharp
// Create transaction
using var transaction = transactionFactory.CreateTransaction();

// Wrap storage provider with TransactionalResource
var transactionalResource = new TransactionalResource<Product>(storageProvider);
transaction.EnlistResource(transactionalResource);

// Stage operations - they won't be committed until transaction commits
transactionalResource.SaveEntity("product-1", new Product { Id = "product-1", Name = "Laptop" });
transactionalResource.SaveEntity("product-2", new Product { Id = "product-2", Name = "Mouse" });
transactionalResource.DeleteEntity("product-old");

// Commit all staged operations atomically
await transaction.CommitAsync();
```

## Key Features

1. **Operation Staging**: Save/delete operations are staged until commit
2. **Transaction Safety**: Operations are only applied if the entire transaction succeeds
3. **Savepoint Support**: Supports nested transactions with rollback points
4. **Provider Agnostic**: Works with any ICrudStorageProvider<T> implementation
5. **Concurrent Safe**: Thread-safe operation staging with proper locking

## Benefits

- Enables ACID transactions for any storage provider
- Provides consistent transactional behavior across different storage types
- Supports complex multi-step operations with rollback capability
- Integrates seamlessly with the Common.Tx transaction system

## Updated POC Services

The CatalogService and CustomerService have been updated to use TransactionalResource<T> for proper transactional behavior:

```csharp
// Before: Direct storage operations
await this.productStore.SaveAsync(product.Id, product);

// After: Transactional operations
var productResource = new TransactionalResource<Product>(this.productStore);
transaction.EnlistResource(productResource);
productResource.SaveEntity(product.Id, product);
await transaction.CommitAsync();
```