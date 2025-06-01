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
1. **Common.Persistence** - Core data persistence library providing abstractions and implementations for reliable data storage
2. **Common.Tx** - Transaction management library for coordinating transactions, including distributed transaction support
3. **ReliableStore** - File-based repository implementation with transactional support

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

When adding new functionality, ensure tests are added to the appropriate test project and follow the existing patterns for unit testing with xUnit.