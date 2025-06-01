# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build entire solution
dotnet build

# Build in Release mode
dotnet build -c Release

# Clean build outputs
dotnet clean
```

## Test Commands

```bash
# Run all tests
dotnet test

# Run tests with code coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test project
dotnet test src/Common.Persistence.Tests/Common.Persistence.Tests.csproj
dotnet test src/Common.Tx.Tests/Common.Tx.Tests.csproj
```

## Architecture Overview

ReliableStore is a .NET 9.0 distributed transaction management solution with microservices demonstrating transactional consistency. The solution includes:

### Core Libraries
1. **Common.Persistence** - Core data persistence library with FileStore implementation for file-based storage and entity definitions (Product, Order, Customer, Payment, Shipment)
2. **Common.Tx** - Advanced transaction management library providing distributed transaction support with ACID guarantees

### Microservices (Proof of Concept)
All services use modern ASP.NET Core hosting with Microsoft.Extensions.Hosting:
- **CatalogService** (Port 9001) - Product catalog APIs
- **CustomerService** (Port 9002) - Customer management APIs  
- **OrderService** (Port 9003) - Order orchestration and transaction coordination
- **PaymentService** (Port 9004) - Payment processing APIs
- **ShippingService** (Port 9005) - Shipping management APIs

### Key Technical Details

- **Target Framework**: .NET 9.0 (globally configured)
- **C# Version**: 10.0
- **Nullable Reference Types**: Enabled across all projects
- **Assembly Naming**: All assemblies prefixed with "CRP."
- **Web Framework**: ASP.NET Core with Microsoft.Extensions.Hosting (migrated from OWIN)
- **Testing Framework**: xUnit with FluentAssertions for readable assertions
- **BDD Testing**: Reqnroll (SpecFlow alternative) available for behavior-driven tests
- **Version Management**: Nerdbank.GitVersioning for automatic semantic versioning

### Build System Features

- Centralized package management via Directory.Packages.props
- Automatic test project detection (*.Tests.csproj pattern)
- StyleCop integration with custom ruleset
- Code coverage via coverlet collector
- Multi-platform support (win-x64, linux-x64, osx-x64)

### Transaction Usage

Services use Common.Tx for distributed transactions:
```csharp
using var transaction = _transactionFactory.CreateTransaction();
transaction.EnlistResource(_fileStore);
await _fileStore.SaveAsync(key, entity);
await transaction.CommitAsync();
```

### Entity Storage

All entities are stored using FileStore<T> from Common.Persistence:
- Products: `data/products.json`
- Orders: `data/orders.json` 
- Customers: `data/customers.json`
- Payments: `data/payments.json`
- Shipments: `data/shipments.json`

### Docker Commands

```bash
# Build and run all services with Docker Compose
docker-compose up --build

# Build specific service Docker image
docker build -f src/CatalogService/Dockerfile -t reliablestore-catalog .

# Run services in background
docker-compose up -d

# View logs
docker-compose logs -f [service-name]

# Stop all services
docker-compose down
```

### GitHub Actions

The repository includes CI/CD workflows:
- **ci-cd.yml**: Builds, tests, and publishes artifacts on push/PR
- **docker-build.yml**: Builds and pushes Docker images to GitHub Container Registry

When adding new functionality, ensure tests are added to the appropriate test project and follow the existing patterns for unit testing with xUnit.