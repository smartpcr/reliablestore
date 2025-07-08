# ReliableStore - Distributed Transaction Management

A high-performance .NET 9.0 distributed transaction management solution with pluggable persistence providers, demonstrating transactional consistency across microservices. The system supports multiple storage backends from ultra-fast in-memory caching to distributed cluster registries, with comprehensive benchmarking showing performance characteristics across different workloads.

## Architecture Overview

ReliableStore implements a sophisticated multi-tier architecture with persistence abstraction, distributed transactions, and microservices coordination:

### Core Libraries

#### Common.Persistence
A provider-based persistence abstraction layer supporting multiple storage backends:
- **Generic Repository Pattern** with compile-time type safety
- **Optimistic Concurrency Control** using sequence numbers
- **Deep Cloning** for entity isolation
- **Dual Serialization Strategy**: Binary BSON for entities, JSON for storage
- **Multiple Storage Providers**: InMemory, FileSystem, ESENT, ClusterRegistry, SQLite

#### Common.Tx
Advanced distributed transaction management implementing:
- **Two-Phase Commit Protocol** for distributed consistency
- **Operation Staging** with in-memory buffering until commit
- **Savepoint Support** for partial rollback capabilities
- **Transaction Isolation** through staged read/write operations
- **Compensation Pattern** for rollback operations

### Microservices
- **CatalogService** (Port 9001) - Product catalog management
- **CustomerService** (Port 9002) - Customer information management
- **OrderService** (Port 9003) - Order orchestration and distributed transaction coordination
- **PaymentService** (Port 9004) - Payment processing with transaction support
- **ShippingService** (Port 9005) - Shipment management with tracking

## Key Features

- ✅ **Distributed Transactions** - ACID compliance across multiple services
- ✅ **File-Based Storage** - Thread-safe JSON persistence with in-memory caching
- ✅ **Modern ASP.NET Core** - .NET 9.0 with Microsoft.Extensions.Hosting
- ✅ **Docker Support** - Multi-stage builds with security best practices
- ✅ **CI/CD Pipelines** - GitHub Actions for build, test, and container publishing
- ✅ **Cross-Service Coordination** - OrderService demonstrates distributed transactions

## Quick Start

### Using Docker Compose (Recommended)

```bash
# Build and run all services
docker-compose up --build

# Run in background
docker-compose up -d

# View logs for specific service
docker-compose logs -f catalog-service

# Stop all services
docker-compose down
```

Services will be available at:
- CatalogService: http://localhost:9001
- CustomerService: http://localhost:9002
- OrderService: http://localhost:9003
- PaymentService: http://localhost:9004
- ShippingService: http://localhost:9005

### Using .NET CLI

```bash
# Build solution
dotnet build

# Run tests
dotnet test

# Start individual service
dotnet run --project src/poc/CatalogService
```

## Example Usage

### Creating a Complete Order (Distributed Transaction)

```bash
# 1. Add a product to catalog
curl -X POST http://localhost:9001/api/catalog/add \
  -H "Content-Type: application/json" \
  -d '{"id":"prod-1","name":"Sample Product","quantity":100,"price":29.99}'

# 2. Add a customer
curl -X POST http://localhost:9002/api/customer/add \
  -H "Content-Type: application/json" \
  -d '{"id":"cust-1","name":"John Doe","email":"john@example.com"}'

# 3. Place an order (creates order, payment, and shipment in single transaction)
curl -X POST http://localhost:9003/api/process/place-order \
  -H "Content-Type: application/json" \
  -d '{"id":"order-1","customerId":"cust-1","productId":"prod-1","quantity":2,"totalAmount":59.98}'
```

## Development

### Prerequisites
- .NET 9.0 SDK
- Docker (optional, for containerized development)

### Project Structure
```
src/
├── Common.Persistence/         # File storage and entities
├── Common.Tx/                 # Transaction management
└── poc/                       # Proof of Concept microservices
    ├── CatalogService/        # Product catalog APIs
    ├── CustomerService/       # Customer management APIs
    ├── OrderService/          # Order coordination and distributed transactions
    ├── PaymentService/        # Payment processing APIs
    └── ShippingService/       # Shipping and tracking APIs

.github/workflows/
├── ci-cd.yml                  # Build, test, and publish
└── docker-build.yml           # Docker image builds
```

### GitHub Actions

The repository includes automated CI/CD pipelines:

- **Continuous Integration**: Builds, tests, and publishes artifacts on every push
- **Docker Publishing**: Builds and pushes container images to GitHub Container Registry
- **Multi-platform Support**: Creates images for both AMD64 and ARM64 architectures

### Data Persistence

Each service stores data in JSON files:
- Products: `data/products.json`
- Customers: `data/customers.json`
- Orders: `data/orders.json`
- Payments: `data/payments.json`
- Shipments: `data/shipments.json`

## Testing

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test project
dotnet test src/Common.Tx.Tests
```

## Performance Benchmarks

### Overview
Comprehensive benchmarks across different storage providers show dramatic performance differences based on workload characteristics and payload sizes. Tests include sequential writes/reads, mixed operations (70% read, 20% write, 10% delete), batch operations, and bulk retrieval.

### Key Performance Metrics (50 Operations, Small Payload)

| Provider | Small (1 KB) | Medium (100 KB) | Large (5 MB) | Scaling Factor | Memory (large) |
|----------|-----------------|-----------------|------------------|----------|----------|
| **InMemory** | 173 μs | 175 μs | 178 μs | 1.0x (no degradation) | 0 GB |
| **FileSystem** | 57 ms | 80 ms | 650 ms | 11.4x | 4.0 GB | |
| **ESENT** | 103 ms | 494 ms | 29.4 s | 285x, high disk io | 4.0 GB |
| **ClusterRegistry** | 1.0 ms | 10 ms | 586 ms | 586x (pool concurrency) | |
| **SQLite** | 95 ms | 122 ms | 1.4 s | 14.7x | 3.8 GB |
| **SqlServer** | 66 ms | 130 ms | 56 s | 848x | 4.1 GB |

### Performance Insights

1. **InMemory Provider**: 100-1000x faster than persistent storage, ideal for caching layers
2. **ClusterRegistry**: Excellent for small payloads (<1KB) in Windows Failover Cluster environments
3. **ESENT**: Best Windows-native performance for medium to large datasets
4. **FileSystem**: Consistent 2-3ms performance per operation, good for cross-platform needs
5. **SQLite**: Reliable embedded database option with predictable performance
6. **SqlServer**: Enterprise-grade with connection pooling overhead

### Memory Allocation Patterns

- **InMemory**: Minimal allocations (8-12 KB for 50 operations)
- **FileSystem**: Moderate allocations with GC pressure on large payloads
- **ESENT/SQLite**: Efficient memory usage with connection pooling
- **ClusterRegistry**: Higher allocation due to Windows registry API overhead

### Benchmark Reports
- [FileSystem, ESENT, SQLite, SqlServer Comparison](https://raw.githack.com/smartpcr/reliablestore/main/src/Common.Persistence.Benchmarks/BenchmarkDotNet.Artifacts/results/BenchmarkSummary-2025-07-07b.html)
- [InMemory, FileSystem, ESENT, ClusterRegistry Comparison](https://raw.githack.com/smartpcr/reliablestore/main/src/Common.Persistence.Benchmarks/BenchmarkDotNet.Artifacts/results/BenchmarkSummary-2025-07-08.html)

## Design Principles

### Provider-Based Architecture
The system uses a provider pattern allowing runtime selection of storage backends based on requirements:
- **Configuration-Driven**: JSON-based provider selection
- **Factory Pattern**: Dynamic provider instantiation
- **Common Interface**: Seamless switching between providers
- **Platform-Specific Optimizations**: Leverages native capabilities when available

### Transaction Management
Implements comprehensive distributed transaction support:
- **TransactionCoordinator**: Central transaction management
- **ITransactionalResource**: Interface for transactional resources
- **TransactionalRepository<T>**: Generic wrapper adding transaction support
- **Unit of Work Pattern**: Staged operations with atomic commit

### Multi-Provider Strategy
Different providers for different scenarios:
- **Configuration Data**: ClusterRegistry for Windows HA environments
- **High-Volume Data**: ESENT or SQLite for large datasets
- **Caching Layer**: InMemory provider for performance-critical paths
- **Cross-Platform**: FileSystem provider for maximum compatibility

## Known Limitations & Future Improvements

### Current Limitations
Based on the EntityStore design analysis, several areas need attention:

1. **Transaction System**: Current implementation lacks true two-phase commit for some providers
2. **Query Capabilities**: Limited to basic CRUD operations without complex querying
3. **Concurrency Control**: Inconsistent sequence number handling between providers
4. **Memory Management**: Potential memory leaks in snapshot management for some providers

### Recommended Improvements
1. **Implement True 2PC**: Add proper distributed transaction coordinator
2. **Enhanced Query Support**: Add LINQ provider or query builder pattern
3. **Unified Concurrency**: Standardize optimistic concurrency across all providers
4. **Performance Monitoring**: Add built-in metrics and telemetry support
5. **Migration Tooling**: Automated data migration between providers

## Getting Started with Providers

### Choosing the Right Provider

```csharp
// Ultra-fast caching and testing
services.AddPersistence(options => options.UseInMemory());

// Cross-platform file storage
services.AddPersistence(options => options.UseFileSystem("./data"));

// Windows high-performance scenarios
services.AddPersistence(options => options.UseEsent("./esent.db"));

// Windows Failover Cluster for HA
services.AddPersistence(options => options.UseClusterRegistry("MyCluster"));

// Embedded database scenarios
services.AddPersistence(options => options.UseSQLite("data.db"));
```

### Transaction Usage Example

```csharp
using var transaction = _transactionFactory.CreateTransaction();

// Enlist multiple resources
transaction.EnlistResource(_productStore);
transaction.EnlistResource(_orderStore);

// Perform operations
await _productStore.UpdateInventoryAsync(productId, -quantity);
await _orderStore.CreateOrderAsync(order);

// Commit all changes atomically
await transaction.CommitAsync();
```
