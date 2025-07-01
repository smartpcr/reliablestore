# EntityStore Design Document

## Overview

The **EntityStore** is a persistence abstraction layer that provides a unified interface for storing and managing entities across different storage backends. It implements the repository pattern with transaction support, optimistic concurrency control, and pluggable storage implementations including Key-Value Store (KVS) and Service Fabric Reliable Collections.

## Architecture

### Core Design Principles

1. **Persistence Abstraction**: Unified interface that can work with multiple storage backends
2. **Transaction Support**: ACID transactions with commit/rollback semantics
3. **Optimistic Concurrency**: Sequence number-based conflict detection
4. **Generic Type Safety**: Strongly-typed entities with compile-time type checking
5. **Pluggable Backends**: Swappable storage implementations (KVS, ReliableCollections)
6. **Deep Cloning**: Entity isolation to prevent unintended mutations

### Architecture Layers

```
┌─────────────────────────────────────────────────────────────┐
│                      Client Code                           │
└─────────────────────────────────────┬───────────────────────┘
                                      │
┌─────────────────────────────────────▼───────────────────────┐
│                 EntityStore Contract                        │
│  IEntityStoreX<TEntity,TKey>  │  ITransactionX              │
│  IEntityTransactionX          │  IEntityX<TKey>             │
└─────────────────────────────────────┬───────────────────────┘
                                      │
┌─────────────────────────────────────▼───────────────────────┐
│               Implementation Layer                          │
│  ┌─────────────────────┐    ┌─────────────────────────────┐ │
│  │   KVS Backend       │    │ ReliableCollections Backend │ │
│  │  - EntityStoreX     │    │  - EntityStoreX             │ │
│  │  - TransactionX     │    │  - TransactionX             │ │
│  │  - Binary Storage   │    │  - Typed Objects            │ │
│  └─────────────────────┘    └─────────────────────────────┘ │
└─────────────────────────────────────┬───────────────────────┘
                                      │
┌─────────────────────────────────────▼───────────────────────┐
│                 Storage Backends                            │
│  ┌─────────────────────┐    ┌─────────────────────────────┐ │
│  │   Key-Value Store   │    │    Service Fabric           │ │
│  │  - InMemoryKvs      │    │  - ReliableStateManager     │ │
│  │  - ClusterRegistry  │    │  - ReliableDictionary       │ │
│  │  - File Storage     │    │  - Cluster/File/InProc      │ │
│  └─────────────────────┘    └─────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

## Core Interfaces

### IEntityX<TKey>
The base entity contract that all stored entities must implement:

```csharp
public interface IEntityX<TKey>
{
    TKey Id { get; set; }              // Primary key
    long SequenceNumber { get; set; }  // Optimistic concurrency control
    IEntityX<TKey> DeepClone();        // Manual deep cloning
}
```

**Key Features:**
- **Generic Key Support**: Supports any comparable and equatable key type
- **Optimistic Concurrency**: SequenceNumber enables conflict detection
- **Deep Cloning**: Manual implementation ensures entity isolation

### IEntityStoreX<TEntity, TKey>
The primary repository interface for entity operations:

```csharp
public interface IEntityStoreX<TEntity, TKey>
    where TEntity : class, IEntityX<TKey>, new()
    where TKey : IComparable<TKey>, IEquatable<TKey>
{
    // Read Operations
    Task<TEntity> Get(TKey key);
    Task<TEntity> Get(TKey key, LockMode lockMode);
    Task<IEnumerable<TEntity>> GetAll();
    
    // Write Operations
    Task Add(TEntity entity);           // Insert (fails if exists)
    Task Set(TEntity entity);          // Upsert (insert or update)
    Task Update(TEntity entity, TEntity comparisonValue); // Optimistic update
    Task Remove(TKey key);              // Delete by key
    Task Remove(TEntity entity);        // Optimistic delete
}
```

**Operation Semantics:**
- **Get**: Retrieves entity by key, returns null if not found
- **Add**: Inserts new entity, throws `EntityStoreXKeyConflictException` if key exists
- **Set**: Upsert operation (insert if new, update if exists)
- **Update**: Optimistic update using comparison entity's SequenceNumber
- **Remove**: Delete operations with optional optimistic concurrency

### ITransactionX
Transaction coordinator that provides EntityStore instances:

```csharp
public interface ITransactionX : IDisposable
{
    Task<IEntityStoreX<TEntity, TKey>> GetEntityStoreX<TEntity, TKey>(
        string storeName, 
        EntityAccessMode entityAccessMode = EntityAccessMode.AlwaysClone);
    
    Task<long> Commit();
}
```

**Transaction Features:**
- **Multi-Store Transactions**: Single transaction can span multiple entity stores
- **Entity Access Modes**: Configurable cloning behavior
- **Resource Management**: Implements IDisposable for proper cleanup

## Storage Backends

### Key-Value Store (KVS) Backend

**Architecture:**
```
EntityStoreX ──► IKeyValueTransaction ──► Storage Implementation
     │                                         │
     ├─ Binary Serialization                   ├─ InMemoryKvsDatabase
     ├─ Exception Handling                     ├─ ClusterRegistry
     └─ Type-based Key Prefixes               └─ File Storage
```

**Key Characteristics:**
- **Binary Serialization**: Uses BSON format via `IEntitySerializer<TEntity>`
- **String Keys**: Converts typed keys to strings with type prefixes
- **Storage Options**: In-memory, cluster registry, or file-based persistence
- **Exception Translation**: Converts KVS exceptions to EntityStore exceptions

**Key Generation Pattern:**
```csharp
// Creates keys like: "MyApp.Models.User/12345"
private static string GetKeyValue(TKey key)
{
    return GetEntityKeyPrefix() + key;
}

private static string GetEntityKeyPrefix()
{
    return typeof(TEntity).ToString() + "/";
}
```

### ReliableCollections Backend

**Architecture:**
```
EntityStoreX ──► ITransaction ──► IReliableStateManager
     │               │                   │
     ├─ Type Safety  ├─ Locking          ├─ IReliableDictionary<TKey,TEntity>
     ├─ Cloning      ├─ Isolation        └─ Persistence Layer
     └─ Exception    └─ Durability              │
       Handling                                 ├─ Cluster Storage
                                                ├─ File Storage
                                                └─ In-Memory
```

**Key Characteristics:**
- **Typed Storage**: Stores entities as strongly-typed objects
- **Service Fabric Integration**: Leverages Service Fabric's reliable collections
- **Advanced Locking**: Supports update locks to prevent deadlocks
- **Entity Access Modes**: Configurable cloning behavior for performance/safety tradeoffs

**Entity Access Modes:**
```csharp
[Flags]
public enum EntityAccessMode
{
    NeverClone = 0,      // Direct references (fastest, least safe)
    CloneOnRead = 1,     // Clone when reading from store
    CloneOnWrite = 2,    // Clone before writing to store
    AlwaysClone = 3      // Clone on both read and write (safest)
}
```

## Serialization Framework

### Dual Serialization Strategy

The EntityStore implements a sophisticated dual serialization approach:

1. **Entity Layer**: Binary BSON serialization for entity transport
2. **Storage Layer**: JSON serialization for persistence backends

**KVS Serialization Pipeline:**
```
Entity ──BSON──► byte[] ──Base64──► JSON ──► Storage
```

**ReliableCollections Pipeline:**
```
Entity ──CloneMode──► TypedObject ──StateSerializer──► Storage
```

### Versioning Support

**Version Header Structure:**
```csharp
// Every serialized entity includes:
// - 16-byte GUID: {C8B232E1-DB7E-4CC9-B7CC-54405DF1861D}
// - Version string length (1 byte)
// - Version string (UTF-8, max 255 bytes)
// - Entity data (BSON)
```

**Backward Compatibility:**
- Maintains map of supported versions to deserializers
- Envelope pattern wraps entities for JSON storage
- Custom `IJsonEntityDeserializer<TEntity>` for version-specific logic

### Deep Cloning Implementation

Entities implement manual deep cloning rather than reflection-based approaches:

```csharp
public IEntityX<TKey> DeepClone()
{
    return new MyEntity
    {
        Id = this.Id,                    // Value types: direct copy
        SequenceNumber = this.SequenceNumber,
        Name = this.Name,                // Strings: immutable reference copy
        Items = this.Items?.ToList(),    // Collections: explicit deep copy
        Child = this.Child?.DeepClone(), // Objects: recursive deep copy
        Dictionary = this.Dictionary?.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToList())   // Nested collections: manual deep copy
    };
}
```

**Validation Framework:**
- `EntityXValidator` ensures no shared references
- Validates clone isolation through mutation testing
- Recursive validation for nested objects and collections

## Exception Handling

### Exception Hierarchy

```csharp
EntityStoreXException                    // Base exception
├─ EntityStoreXKeyConflictException     // Optimistic concurrency conflicts
└─ EntityStoreXTransientException       // Temporary failures (retry candidate)
```

### Backend-Specific Exception Mapping

**KVS Backend:**
- `KeyValueStoreConflictException` → `EntityStoreXKeyConflictException`
- `KeyValueStoreException` → `EntityStoreXException`
- `IKvsExceptionHandler` provides centralized exception translation

**ReliableCollections Backend:**
- `ArgumentException` (key exists) → `EntityStoreXKeyConflictException`
- Service Fabric exceptions → `EntityStoreXTransientException`
- `IReliableCollectionsExceptionHandler` handles Service Fabric-specific errors

## Configuration and Factory Pattern

### Backend Selection

```csharp
public enum EntityStoreType
{
    Unspecified = 0,
    KeyValueStore = 1,       // KVS backend
    ReliableCollections = 2  // Service Fabric backend
}
```

### Factory Implementation

The EntityStore uses factory patterns for backend creation:

```csharp
// KVS Factory
IEntityTransactionXFactory<KeyValueStore> kvsFactory;
var transaction = await kvsFactory.CreateTransactionAsync();

// ReliableCollections Factory  
IEntityTransactionXFactory<ReliableCollections> rcFactory;
var transaction = await rcFactory.CreateTransactionAsync();
```

## Transaction Design Analysis

### Critical Transaction Flaws

The EntityStore transaction implementation has **severe limitations** that compromise ACID guarantees:

#### 1. **No True Two-Phase Commit**

**KVS Backend**: Uses snapshot-and-swap pattern, not 2PC:
```csharp
public void Commit(IEnumerable<Action> operations)
{
    var snapshot = GetStoreSnapshot();  // Copy current state
    foreach (var action in operations)
        action(snapshot);               // Apply all changes to snapshot
    this.store = snapshot;              // Atomic swap - single point of failure
}
```

**ReliableCollections Backend**: Delegates to Service Fabric's 2PC but doesn't implement its own coordination.

#### 2. **Broken Lock Mode Support**

**Critical Flaw**: KVS backend completely ignores lock modes:
```csharp
// KVS Implementation - IGNORES lock modes!
public Task<TEntity> Get(TKey key, LockMode lockMode)
{
    // lockMode parameter completely ignored (line 74)
    var value = this.keyValueTransaction.Get(keyValue);
    return Task.FromResult(this.DeserializeEntity(value));
}

// ReliableCollections - Properly supports lock modes
private RC.LockMode TranslateLockMode(ES.LockMode lockMode)
{
    switch (lockMode)
    {
        case ES.LockMode.Update:    // Prevents read-upgrade deadlocks
            return RC.LockMode.Update;
        case ES.LockMode.Default:
            return RC.LockMode.Default;
    }
}
```

**Impact**: Deadlock prevention is completely broken in KVS backend.

#### 3. **Inconsistent Sequence Number Handling**

```csharp
// KVS: Properly manages sequence numbers
entity.SequenceNumber = kvsEntry.SequenceNumber;

// ReliableCollections: Returns 0, doesn't track sequence numbers!
public async Task<long> Commit()
{
    await this.reliableStateTransaction.CommitAsync();
    return 0;  // Sequence number lost!
}
```

**Impact**: Optimistic concurrency control is inconsistent between backends.

#### 4. **No Multi-Entity Transaction Ordering**

```csharp
// This pattern can deadlock:
var entity1 = await store1.Get(key1, LockMode.Update); // Lock ignored in KVS!
var entity2 = await store2.Get(key2, LockMode.Update); // Lock ignored in KVS!
// Concurrent transactions can deadlock here with no deterministic ordering
```

### Isolation Level Analysis

**KVS Backend:**
- **Isolation Level**: Snapshot isolation
- **Dirty Reads**: Prevented ✅
- **Non-repeatable Reads**: Prevented ✅
- **Phantom Reads**: Possible ❌
- **Locking**: None (optimistic only)

**ReliableCollections Backend:**
- **Isolation Level**: Delegates to Service Fabric (Read Committed)
- **Locking**: Proper pessimistic locking support
- **Deadlock Prevention**: Update locks supported

### Concurrency Control Weaknesses

1. **Optimistic-Only in KVS**: No pessimistic locking support
2. **Retry Storms**: High contention triggers excessive retries
3. **No Timeout Mechanisms**: Potential for infinite retry loops
4. **Memory Pressure**: Full store snapshots for each KVS transaction

## Design Issues and Improvement Areas

### Current Issues

#### 1. **Transaction System Fundamentally Flawed**

**Current Implementation Problems:**
- No proper 2PC coordination between backends
- Broken lock mode support in KVS
- Inconsistent isolation guarantees
- **Impact**: ACID guarantees compromised

**Specific Code Issues:**
```csharp
// src/Persistence/EntityStore/Kvs/Source/EntityStoreX.cs:78
public Task<TEntity> Get(TKey key, LockMode lockMode)
{
    // CRITICAL BUG: lockMode parameter completely ignored!
    var keyValue = GetKeyValue(key);
    var value = this.keyValueTransaction.Get(keyValue);
    return Task.FromResult(this.DeserializeEntity(value));
}
```

**Fix Required:**
```csharp
public Task<TEntity> Get(TKey key, LockMode lockMode)
{
    var keyValue = GetKeyValue(key);
    
    // Implement lock mode support using ReaderWriterLockSlim
    if (lockMode == LockMode.Update)
    {
        this.lockManager.EnterUpgradeableReadLock(keyValue);
    }
    else
    {
        this.lockManager.EnterReadLock(keyValue);
    }
    
    try
    {
        var value = this.keyValueTransaction.Get(keyValue);
        return Task.FromResult(this.DeserializeEntity(value));
    }
    finally
    {
        // Locks released on transaction commit/rollback
    }
}
```

#### 2. **Deadlock Prevention Broken**

**Current Implementation:**
- KVS ignores all lock modes (line 78 in `EntityStoreX.cs`)
- No deterministic key ordering for multi-entity operations
- **Impact**: High risk of deadlocks in concurrent scenarios

**Example Deadlock Scenario:**
```csharp
// Transaction 1:
var entity1 = await store.Get("key1", LockMode.Update); // No lock acquired in KVS!
var entity2 = await store.Get("key2", LockMode.Update); // No lock acquired in KVS!

// Transaction 2 (concurrent):
var entity2 = await store.Get("key2", LockMode.Update); // No lock acquired in KVS!
var entity1 = await store.Get("key1", LockMode.Update); // No lock acquired in KVS!
// DEADLOCK: Both transactions try to update both entities simultaneously
```

**Fix Required:**
```csharp
public class KvsLockManager
{
    private readonly ConcurrentDictionary<string, ReaderWriterLockSlim> keyLocks 
        = new ConcurrentDictionary<string, ReaderWriterLockSlim>();
    
    public void AcquireLock(string key, LockMode lockMode, TimeSpan timeout)
    {
        var lockObj = keyLocks.GetOrAdd(key, _ => new ReaderWriterLockSlim());
        
        switch (lockMode)
        {
            case LockMode.Update:
                if (!lockObj.TryEnterUpgradeableReadLock(timeout))
                    throw new TimeoutException($"Failed to acquire update lock on key: {key}");
                break;
            case LockMode.Default:
                if (!lockObj.TryEnterReadLock(timeout))
                    throw new TimeoutException($"Failed to acquire read lock on key: {key}");
                break;
        }
    }
}

// Multi-entity operations with deterministic ordering
public async Task UpdateManyAsync<T>(params (TKey key, T entity)[] updates)
{
    // Sort keys to prevent deadlocks
    var sortedUpdates = updates.OrderBy(u => u.key.ToString()).ToArray();
    
    foreach (var update in sortedUpdates)
    {
        lockManager.AcquireLock(update.key.ToString(), LockMode.Update, TimeSpan.FromSeconds(30));
    }
    
    // Proceed with updates...
}
```

#### 3. **Inconsistent Sequence Number Handling**

**Current Implementation Problems:**

**KVS Backend** (`src/Persistence/EntityStore/Kvs/Source/EntityStoreX.cs:243`):
```csharp
private TEntity DeserializeEntity(IKvsEntry kvsEntry)
{
    // CORRECT: Assigns sequence number from storage
    entity.SequenceNumber = kvsEntry.SequenceNumber;
    return entity;
}
```

**ReliableCollections Backend** (`src/Persistence/EntityStore/ReliableCollections/Source/TransactionX.cs:61`):
```csharp
public async Task<long> Commit()
{
    await this.reliableStateTransaction.CommitAsync();
    return 0;  // BUG: Always returns 0, loses sequence number!
}
```

**KVS Transaction Commit** (`src/Persistence/Kvs/InMemoryKvs/Source/InMemoryKeyValueTransaction.cs`):
```csharp
public Task<long> Commit()
{
    // CORRECT: Returns actual sequence number
    var sequenceNumber = this.database.GetNewSequenceNumber();
    // ... apply operations ...
    return Task.FromResult(sequenceNumber);
}
```

**Impact Analysis:**
- **KVS**: Sequence numbers work correctly for optimistic concurrency
- **ReliableCollections**: Sequence numbers are lost, breaking optimistic concurrency
- **Cross-backend inconsistency**: Same interface, different behavior

**Fix Required:**

```csharp
// Fix ReliableCollections TransactionX.cs
public async Task<long> Commit()
{
    await this.reliableStateTransaction.CommitAsync();
    
    // FIXED: Return actual sequence number from reliable state manager
    return this.reliableStateManager.GetLastCommittedSequenceNumber();
}

// Enhance IReliableStateManager contract
public interface IReliableStateManager
{
    long GetLastCommittedSequenceNumber();
    long GetNewSequenceNumber();
    // ... existing methods
}

// Update entity sequence numbers on all operations
public async Task<TEntity> Get(TKey key, LockMode lockMode)
{
    var value = await this.reliableDictionary.TryGetValueAsync(this.reliableStateTransaction, key);
    var entity = value.HasValue ? value.Value : null;
    
    if (entity != null)
    {
        // FIXED: Assign current sequence number
        entity.SequenceNumber = this.reliableStateManager.GetCurrentSequenceNumber(key);
    }
    
    return this.ConditionalCloneEntity(entity, this.entityAccessMode.HasFlag(EntityAccessMode.CloneOnRead));
}
```

#### 4. **Broken Optimistic Concurrency in ReliableCollections**

**Current Implementation** (`src/Persistence/EntityStore/ReliableCollections/Source/EntityStoreX.cs:164`):
```csharp
public async Task Update(TEntity entity, TEntity comparisonValue)
{
    // BUG: Uses object reference comparison, not sequence numbers!
    if (!await this.reliableDictionary.TryUpdateAsync(
        this.reliableStateTransaction, entity.Id, entity, comparisonValue))
    {
        throw new EntityStoreXKeyConflictException();
    }
}
```

**Problem**: ReliableCollections uses object reference equality for `comparisonValue`, not sequence number comparison like KVS.

**Fix Required:**
```csharp
public async Task Update(TEntity entity, TEntity comparisonValue)
{
    // FIXED: Implement proper sequence number-based optimistic concurrency
    var currentEntity = await this.reliableDictionary.TryGetValueAsync(
        this.reliableStateTransaction, entity.Id);
    
    if (!currentEntity.HasValue)
    {
        throw new EntityStoreXKeyConflictException("Entity not found");
    }
    
    if (currentEntity.Value.SequenceNumber != comparisonValue.SequenceNumber)
    {
        throw new EntityStoreXKeyConflictException(
            $"Sequence number mismatch. Expected: {comparisonValue.SequenceNumber}, " +
            $"Actual: {currentEntity.Value.SequenceNumber}");
    }
    
    // Update with new sequence number
    entity.SequenceNumber = this.reliableStateManager.GetNewSequenceNumber();
    await this.reliableDictionary.SetAsync(this.reliableStateTransaction, entity.Id, entity);
}
```

#### 5. **Entity Access Mode Inconsistencies**

**Current Implementation Problem** (`src/Persistence/EntityStore/ReliableCollections/Source/EntityStoreX.cs:166`):
```csharp
public async Task Update(TEntity entity, TEntity comparisonValue)
{
    if (this.entityAccessMode.HasFlag(EntityAccessMode.CloneOnRead))
    {
        // BUG: Throws exception instead of handling the scenario properly
        throw new InvalidOperationException(
            "EntityStoreX.Update is not supported when CloneOnRead behavior is specified.");
    }
}
```

**Problem**: CloneOnRead mode makes Update operations impossible instead of handling them properly.

**Fix Required:**
```csharp
public async Task Update(TEntity entity, TEntity comparisonValue)
{
    // FIXED: Handle CloneOnRead mode properly
    TEntity currentEntity;
    if (this.entityAccessMode.HasFlag(EntityAccessMode.CloneOnRead))
    {
        // Get original entity for comparison
        var originalEntity = await this.reliableDictionary.TryGetValueAsync(
            this.reliableStateTransaction, entity.Id);
        
        if (!originalEntity.HasValue)
            throw new EntityStoreXKeyConflictException("Entity not found");
            
        currentEntity = originalEntity.Value;
        
        // Compare sequence numbers instead of object references
        if (currentEntity.SequenceNumber != comparisonValue.SequenceNumber)
            throw new EntityStoreXKeyConflictException("Optimistic concurrency conflict");
    }
    else
    {
        // Existing logic for non-cloning mode
        currentEntity = comparisonValue;
    }
    
    entity = this.ConditionalCloneEntity(entity, this.entityAccessMode.HasFlag(EntityAccessMode.CloneOnWrite));
    entity.SequenceNumber = this.reliableStateManager.GetNewSequenceNumber();
    await this.reliableDictionary.SetAsync(this.reliableStateTransaction, entity.Id, entity);
}
```

#### 6. **Memory Leaks in KVS Snapshot Management**

**Current Implementation** (`src/Persistence/Kvs/InMemoryKvs/Source/InMemoryKvsDatabase.cs`):
```csharp
private ConcurrentDictionary<string, InMemoryKvsEntry> GetStoreSnapshot()
{
    // BUG: Creates full copy of entire store for every transaction
    var snapshot = new ConcurrentDictionary<string, InMemoryKvsEntry>();
    foreach (var kvp in this.store)
    {
        snapshot[kvp.Key] = new InMemoryKvsEntry(kvp.Value.Value, kvp.Value.SequenceNumber);
    }
    return snapshot;
}
```

**Problem**: Creates full store copies causing memory pressure with large datasets.

**Fix Required:**
```csharp
// Implement copy-on-write semantics
public class CopyOnWriteStore
{
    private readonly ConcurrentDictionary<string, InMemoryKvsEntry> baseStore;
    private readonly ConcurrentDictionary<string, InMemoryKvsEntry> modifications;
    private readonly ConcurrentDictionary<string, bool> deletions;
    
    public InMemoryKvsEntry Get(string key)
    {
        // Check deletions first
        if (deletions.ContainsKey(key)) return null;
        
        // Check modifications
        if (modifications.TryGetValue(key, out var modified)) return modified;
        
        // Fall back to base store
        return baseStore.TryGetValue(key, out var original) ? original : null;
    }
    
    public void Set(string key, InMemoryKvsEntry value)
    {
        deletions.TryRemove(key, out _);
        modifications[key] = value;
    }
    
    public void Delete(string key)
    {
        modifications.TryRemove(key, out _);
        deletions[key] = true;
    }
}
```

#### 7. **Limited Query Capabilities**

**Current Implementation**: Only supports basic CRUD operations
- `Get(key)` - Single entity retrieval
- `GetAll()` - Full table scan only
- No filtering, sorting, or partial loading

**Impact**: Forces full collection enumeration for complex queries

**Fix Required:**
```csharp
public interface IEntityStoreX<TEntity, TKey> : IQueryable<TEntity>
{
    // Existing methods...
    
    // Enhanced query capabilities
    Task<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate);
    Task<TEntity> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate);
    Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate);
    
    // Pagination support
    Task<(IEnumerable<TEntity> items, string? nextToken)> GetPagedAsync(
        int pageSize, string? continuationToken = null);
    
    // Projection support
    Task<IEnumerable<TResult>> SelectAsync<TResult>(Expression<Func<TEntity, TResult>> selector);
}
```

#### 8. **Manual Deep Cloning Burden**

**Current Implementation**: Requires manual `DeepClone()` implementation:
```csharp
public IEntityX<TKey> DeepClone()
{
    return new MyEntity
    {
        Id = this.Id,
        SequenceNumber = this.SequenceNumber,
        Items = this.Items?.ToList(),    // Manual collection copying
        Child = this.Child?.DeepClone(), // Manual recursive copying
        Dictionary = this.Dictionary?.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToList())   // Manual nested copying
    };
}
```

**Problems**:
- Error-prone manual implementation
- Inconsistent across entity types
- High maintenance overhead
- Performance implications

**Fix Required:**
```csharp
// Automatic deep cloning via serialization
public static class EntityCloner
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.Preserve // Handle circular references
    };
    
    public static T DeepClone<T>(T entity) where T : class
    {
        if (entity == null) return null;
        
        var json = JsonSerializer.Serialize(entity, Options);
        return JsonSerializer.Deserialize<T>(json, Options);
    }
}

// Remove manual DeepClone requirement from IEntityX
public interface IEntityX<TKey>
{
    TKey Id { get; set; }
    long SequenceNumber { get; set; }
    // DeepClone() method removed - handled automatically
}
```

#### 9. **Exception Translation Complexity**

**Current Implementation**: Each backend requires custom exception handlers with inconsistent mapping.

**KVS Exception Handler** (`src/Persistence/EntityStore/Kvs/Source/KvsExceptionHandler.cs`):
```csharp
public Task<T> Invoke<T>(Func<Task<T>> operation)
{
    try
    {
        return operation();
    }
    catch (KeyValueStoreConflictException)
    {
        throw new EntityStoreXKeyConflictException();
    }
    catch (KeyValueStoreAbortedException)
    {
        throw new EntityStoreXTransientException();
    }
    // Inconsistent exception mapping...
}
```

**Fix Required:**
```csharp
public class StandardizedExceptionHandler
{
    private readonly ILogger logger;
    
    public async Task<T> ExecuteWithExceptionTranslation<T>(
        Func<Task<T>> operation, 
        string operationName, 
        string entityType)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex) when (IsOptimisticConcurrencyConflict(ex))
        {
            logger.LogWarning(ex, "Optimistic concurrency conflict in {Operation} for {EntityType}", 
                operationName, entityType);
            throw new EntityStoreXKeyConflictException(
                $"Optimistic concurrency conflict during {operationName}", ex);
        }
        catch (Exception ex) when (IsTransientFailure(ex))
        {
            logger.LogWarning(ex, "Transient failure in {Operation} for {EntityType}", 
                operationName, entityType);
            throw new EntityStoreXTransientException(
                $"Transient failure during {operationName}", ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error in {Operation} for {EntityType}", 
                operationName, entityType);
            throw new EntityStoreXException(
                $"Unexpected error during {operationName}", ex);
        }
    }
    
    private bool IsOptimisticConcurrencyConflict(Exception ex)
    {
        return ex is KeyValueStoreConflictException 
            || ex is ArgumentException 
            || (ex.Message?.Contains("conflict", StringComparison.OrdinalIgnoreCase) == true);
    }
    
    private bool IsTransientFailure(Exception ex)
    {
        return ex is TimeoutException 
            || ex is TaskCanceledException
            || ex is KeyValueStoreAbortedException;
    }
}
```

### Performance Characteristics

**KVS Backend:**
- **Pros**: Simple key-value operations, consistent performance
- **Cons**: Binary serialization overhead, string key conversion

**ReliableCollections Backend:**
- **Pros**: Typed storage, advanced locking, Service Fabric integration
- **Cons**: Complex transaction semantics, memory overhead

**Memory Usage Pattern:**
```
AlwaysClone Mode: 3x memory usage (original + read clone + write clone)
NeverClone Mode: 1x memory usage (direct references)
```

## Extraction Strategy for Standalone Library

### Target Framework Migration

**Current State:** .NET Framework 4.6.2 / .NET Standard 2.0
**Target State:** .NET 4.7.2, .NET 8.0, .NET 9.0

### Dependency Reduction Plan

1. **Remove Service Fabric Dependencies**
   - Abstract away `ITransaction` and `IReliableStateManager`
   - Create generic transaction interfaces
   - Implement in-memory and file-based alternatives

2. **Simplify Serialization Stack**
   - Replace BSON with System.Text.Json
   - Implement automatic deep cloning via serialization
   - Add support for modern serialization attributes

3. **Modernize Exception Handling**
   - Use standard .NET exception types where possible
   - Implement structured exception translation
   - Add support for nullable reference types

4. **Improve Query Capabilities**
   - Add LINQ-style query interface
   - Implement filtering and pagination
   - Support projection and selective loading

### Recommended Architecture for Standalone Library

#### Proper Transaction Design

```csharp
public interface ITransaction : IDisposable
{
    // Proper isolation levels
    IsolationLevel IsolationLevel { get; }
    
    // Timeout for deadlock prevention  
    TimeSpan Timeout { get; set; }
    
    // Multi-entity operations with deterministic ordering
    Task<bool> TryUpdateManyAsync<T>(
        IEnumerable<(T entity, T comparison)> updates,
        CancellationToken cancellationToken = default);
    
    // Proper 2PC support
    Task<bool> PrepareAsync();
    Task CommitAsync();
    Task RollbackAsync();
    
    // Lock escalation control with timeouts
    Task<T> GetWithLockAsync<T>(TKey key, LockMode lockMode, TimeSpan? timeout = null);
}
```

#### Modern Entity Store Interface

```csharp
// Modern interface design with proper concurrency support
public interface IEntityStore<TEntity, TKey> 
    where TEntity : class, IEntity<TKey>
    where TKey : IEquatable<TKey>, IComparable<TKey>
{
    // Async enumerable for better performance
    IAsyncEnumerable<TEntity> GetAllAsync(CancellationToken cancellationToken = default);
    
    // Optional support for nullable entities
    Task<TEntity?> GetAsync(TKey key, CancellationToken cancellationToken = default);
    
    // LINQ-style querying
    IQueryable<TEntity> Where(Expression<Func<TEntity, bool>> predicate);
    
    // Batch operations with proper transaction coordination
    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    
    // Modern optimistic concurrency with ETags
    Task<bool> TryUpdateAsync(TEntity entity, string? expectedETag = null, CancellationToken cancellationToken = default);
    
    // Pessimistic locking support
    Task<TEntity?> GetWithLockAsync(TKey key, LockMode lockMode, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}
```

### Critical Fixes Required for Extraction

#### 1. Fix Transaction System
- **Implement proper 2PC coordination** across multiple stores
- **Add lock mode support** to KVS backend using read-write locks
- **Implement deterministic key ordering** for multi-entity operations
- **Add transaction timeouts** for deadlock prevention

#### 2. Consistent Sequence Number Handling
- **Standardize sequence number management** across all backends
- **Implement global sequence number generation** for cross-backend consistency
- **Add ETag-based optimistic concurrency** as modern alternative

#### 3. Enhanced Concurrency Control
```csharp
// Proper multi-entity update with ordering
public async Task<bool> UpdateManyAsync<T>(params (TKey key, T entity, T comparison)[] updates)
{
    // Sort by key to prevent deadlocks
    var sortedUpdates = updates.OrderBy(u => u.key);
    
    // Acquire locks in deterministic order
    var lockedEntities = new List<T>();
    try
    {
        foreach (var update in sortedUpdates)
        {
            var entity = await GetWithLockAsync(update.key, LockMode.Update, TimeSpan.FromSeconds(30));
            lockedEntities.Add(entity);
        }
        
        // Perform updates
        // ...
        
        await CommitAsync();
        return true;
    }
    catch (TimeoutException)
    {
        await RollbackAsync();
        return false; // Deadlock detected and resolved
    }
}
```

### Migration Path

1. **Phase 1 - Critical Fixes**: 
   - Fix broken lock mode support in KVS
   - Implement consistent sequence number handling
   - Add transaction timeout mechanisms

2. **Phase 2 - Transaction Redesign**:
   - Implement proper 2PC coordination
   - Add deterministic key ordering for multi-entity operations
   - Enhance isolation level support

3. **Phase 3 - Modern Features**:
   - Replace BSON with System.Text.Json
   - Implement automatic deep cloning via serialization
   - Add ETag-based optimistic concurrency

4. **Phase 4 - Enhanced Backends**:
   - Add modern storage backends (SQLite, LiteDB, memory)
   - Implement LINQ query provider
   - Add async enumerable support

5. **Phase 5 - Final Polish**:
   - Add nullable reference type support
   - Implement comprehensive performance monitoring
   - Add distributed transaction coordinator for cross-service scenarios

**Priority**: The transaction system flaws are **critical** and must be fixed before any extraction attempt, as they compromise fundamental ACID guarantees.