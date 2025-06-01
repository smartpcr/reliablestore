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

ReliableStore is a .NET 8.0/9.0 solution providing reliable data storage and transaction management capabilities. The solution consists of two main components:

1. **Common.Persistence** - Core data persistence library providing abstractions and implementations for reliable data storage
2. **Common.Tx** - Transaction management library for coordinating transactions, potentially including distributed transaction support

### Key Technical Details

- **Target Frameworks**: .NET 8.0 (default), some projects target .NET 9.0
- **C# Version**: 10.0
- **Nullable Reference Types**: Enabled across all projects
- **Assembly Naming**: All assemblies prefixed with "CRP."
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