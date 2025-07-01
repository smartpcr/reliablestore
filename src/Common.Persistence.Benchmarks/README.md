# Common.Persistence.Benchmarks

Performance benchmarks for different persistence providers using BenchmarkDotNet.

## Overview

This project contains comprehensive benchmarks for testing the performance of different persistence providers under various conditions:

- **Operation Counts**: 1k, 10k, 100k operations
- **Payload Sizes**: Small (~100 bytes), Medium (~1KB), Large (~10KB)
- **CPU Cores**: 2, 4, 8, 16 cores (throttled environments)
- **Providers**: InMemory, FileSystem, ESENT, ClusterRegistry

## Running Benchmarks

### Sequential Benchmarks

```bash
dotnet run -c Release
```

### Concurrent Benchmarks

```bash
dotnet run -c Release -- --concurrent
```

## Benchmark Categories

### Sequential Benchmarks (ProviderBenchmarks)

1. **Sequential Write Operations**: Measures write throughput
2. **Sequential Read Operations**: Measures read throughput after initial writes
3. **Mixed Operations**: 70% reads, 20% writes, 10% deletes
4. **Batch Operations**: Parallel batch processing
5. **GetAll Operation**: Tests bulk retrieval performance

### Concurrent Benchmarks (ConcurrentProviderBenchmarks)

1. **Concurrent Write Operations**: Multiple threads writing simultaneously
2. **Concurrent Read Operations**: Multiple threads reading simultaneously
3. **Concurrent Mixed Operations**: Multiple threads performing mixed operations

## Parameters

### OperationCount
- 1,000 operations (1k)
- 10,000 operations (10k)
- 100,000 operations (100k)

### PayloadSize
- **Small**: ~100 bytes per record
- **Medium**: ~1KB per record
- **Large**: ~10KB per record

### CoreCount (Sequential only)
- 2 cores
- 4 cores
- 8 cores
- 16 cores

### ThreadCount (Concurrent only)
- 2 threads
- 8 threads
- 16 threads

### ProviderType
- **InMemory**: In-memory storage provider
- **FileSystem**: File-based storage provider
- **Esent**: Windows ESENT database provider (Windows only)
- **ClusterRegistry**: Windows Failover Cluster registry provider (Windows only)

## Output

BenchmarkDotNet generates detailed reports including:
- Mean execution time
- Standard deviation
- Memory allocation
- Thread statistics
- Detailed HTML reports in `BenchmarkDotNet.Artifacts` folder

## Requirements

- .NET 9.0 SDK
- Windows OS (for ESENT and ClusterRegistry providers)
- Administrator privileges (recommended for accurate CPU affinity)

## Notes

- The benchmark automatically sets CPU affinity to limit core usage
- ESENT and ClusterRegistry providers are skipped on non-Windows platforms
- Each benchmark run creates isolated temporary directories for data storage
- Session pooling is enabled for ESENT provider to improve performance