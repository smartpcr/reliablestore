//-------------------------------------------------------------------------------
// <copyright file="SQLiteProviderPerformanceTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.SQLite.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Threading.Tasks;
    using AwesomeAssertions;
    using Common.Persistence.Configuration;
    using Common.Persistence.Factory;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Models;
    using Xunit;
    using Xunit.Abstractions;

    [Trait("Category", "Performance")]
    public class SQLiteProviderPerformanceTests : IAsyncLifetime, IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly string providerName = "SQLitePerfTest";
        private readonly string schemaName = "test3";
        private readonly string databasePath;
        private IServiceProvider serviceProvider;
        private ICrudStorageProviderFactory? factory;

        public SQLiteProviderPerformanceTests(ITestOutputHelper output)
        {
            this.output = output;
            this.databasePath = Path.Combine(Directory.GetCurrentDirectory(), $"test_{Guid.NewGuid():N}.db");
        }

        public Task InitializeAsync()
        {
            // Setup DI container
            var services = new ServiceCollection();
            services.AddLogging(builder => builder
                .SetMinimumLevel(LogLevel.Warning) // Less logging for performance tests
                .AddXunit(this.output));

            // Configure SQLite provider
            var config = new Dictionary<string, string>
            {
                [$"Providers:{this.providerName}:Name"] = this.providerName,
                [$"Providers:{this.providerName}:AssemblyName"] = "CRP.Common.Persistence.Providers.SQLite",
                [$"Providers:{this.providerName}:TypeName"] = "Common.Persistence.Providers.SQLite.SQLiteProvider`1",
                [$"Providers:{this.providerName}:Enabled"] = "true",
                [$"Providers:{this.providerName}:Capabilities"] = "1",
                [$"Providers:{this.providerName}:DataSource"] = this.databasePath,
                [$"Providers:{this.providerName}:Mode"] = "ReadWriteCreate",
                [$"Providers:{this.providerName}:Cache"] = "Shared",
                [$"Providers:{this.providerName}:ForeignKeys"] = "true",
                [$"Providers:{this.providerName}:CommandTimeout"] = "300", // Higher timeout for perf tests
                [$"Providers:{this.providerName}:CreateTableIfNotExists"] = "true",
                [$"Providers:{this.providerName}:Schema"] = this.schemaName
            };

            var configuration = services.AddConfiguration(config);

            // Register settings
            var settings = configuration.GetConfiguredSettings<SQLiteProviderSettings>($"Providers:{this.providerName}");
            services.AddKeyedSingleton<CrudStorageProviderSettings>(this.providerName, (_, _) => settings);
            services.AddSingleton<IConfigReader, JsonConfigReader>();
            services.AddPersistence();

            this.serviceProvider = services.BuildServiceProvider();
            this.factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            // Clear all data in the schema
            await this.ClearAllTablesInSchemaAsync();

            // Dispose the service provider to ensure all connections are closed
            if (this.serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }

            // Force garbage collection to ensure SQLite connections are released
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Delete the database file with retry
            var maxRetries = 5;
            var retryDelay = TimeSpan.FromMilliseconds(500);

            for (var i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (File.Exists(this.databasePath))
                    {
                        File.Delete(this.databasePath);
                        break; // Success, exit loop
                    }
                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    // Wait before retrying
                    await Task.Delay(retryDelay);
                }
            }
        }

        public void Dispose()
        {
            (this.serviceProvider as IDisposable)?.Dispose();
        }

        private async Task ClearAllTablesInSchemaAsync()
        {
            try
            {
                using var provider = this.factory?.Create<Product>(this.providerName);
                if (provider != null)
                {
                    await provider.ClearAsync();
                }
            }
            catch (Exception ex)
            {
                this.output.WriteLine($"Error clearing tables: {ex.Message}");
            }
        }

        [Fact]
        public async Task BulkInsert_Performance()
        {
            // Arrange
            using var provider = this.factory!.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            var recordCounts = new[] { 100, 1000, 5000 };
            var results = new List<(int Count, double TotalMs, double PerRecordMs)>();

            foreach (var count in recordCounts)
            {
                await provider.ClearAsync();

                var products = Enumerable.Range(1, count).Select(i => new Product
                {
                    Id = $"bulk-perf-{i:D6}",
                    Name = $"Bulk Performance Product {i}",
                    Description = $"This is a detailed description for product {i} used in performance testing",
                    Quantity = i % 1000,
                    Price = (i % 100) * 9.99m,
                    Tags = new List<string> { $"tag{i % 10}", $"category{i % 5}", "performance" }
                }).ToList();

                var entities = products.Select(p => new KeyValuePair<string, Product>(p.Key, p)).ToList();

                // Act
                var sw = Stopwatch.StartNew();
                await provider.SaveManyAsync(entities);
                sw.Stop();

                var perRecord = sw.Elapsed.TotalMilliseconds / count;
                results.Add((count, sw.Elapsed.TotalMilliseconds, perRecord));

                this.output.WriteLine($"Bulk insert {count} records: {sw.Elapsed.TotalMilliseconds:F2}ms total, {perRecord:F3}ms per record");
            }

            // Assert
            results.Should().AllSatisfy(r => r.PerRecordMs.Should().BeLessThan(50)); // Should be fast
        }

        [Fact]
        public async Task SingleRecordOperations_Performance()
        {
            // Arrange
            using var provider = this.factory!.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            const int operationCount = 1000;
            var products = new List<Product>();

            // Measure insert performance
            var insertSw = Stopwatch.StartNew();
            for (int i = 0; i < operationCount; i++)
            {
                var product = new Product
                {
                    Id = $"single-perf-{i:D6}",
                    Name = $"Single Performance Product {i}",
                    Quantity = i
                };
                products.Add(product);
                await provider.SaveAsync(product.Key, product);
            }
            insertSw.Stop();

            // Measure read performance
            var readSw = Stopwatch.StartNew();
            foreach (var product in products)
            {
                await provider.GetAsync(product.Key);
            }
            readSw.Stop();

            // Measure update performance
            var updateSw = Stopwatch.StartNew();
            foreach (var product in products)
            {
                product.Quantity++;
                await provider.SaveAsync(product.Key, product);
            }
            updateSw.Stop();

            // Measure delete performance
            var deleteSw = Stopwatch.StartNew();
            foreach (var product in products)
            {
                await provider.DeleteAsync(product.Key);
            }
            deleteSw.Stop();

            // Output results
            this.output.WriteLine($"Single record operations ({operationCount} records):");
            this.output.WriteLine($"  Insert: {insertSw.Elapsed.TotalMilliseconds:F2}ms total, {insertSw.Elapsed.TotalMilliseconds / operationCount:F3}ms per op");
            this.output.WriteLine($"  Read: {readSw.Elapsed.TotalMilliseconds:F2}ms total, {readSw.Elapsed.TotalMilliseconds / operationCount:F3}ms per op");
            this.output.WriteLine($"  Update: {updateSw.Elapsed.TotalMilliseconds:F2}ms total, {updateSw.Elapsed.TotalMilliseconds / operationCount:F3}ms per op");
            this.output.WriteLine($"  Delete: {deleteSw.Elapsed.TotalMilliseconds:F2}ms total, {deleteSw.Elapsed.TotalMilliseconds / operationCount:F3}ms per op");

            // Assert - SQLite performance expectations (more relaxed due to synchronous disk writes)
            (insertSw.Elapsed.TotalMilliseconds / operationCount).Should().BeLessThan(20);
            (readSw.Elapsed.TotalMilliseconds / operationCount).Should().BeLessThan(5);
            (updateSw.Elapsed.TotalMilliseconds / operationCount).Should().BeLessThan(20);
            (deleteSw.Elapsed.TotalMilliseconds / operationCount).Should().BeLessThan(20);
        }

        [Fact]
        public async Task QueryPerformance_WithPredicate()
        {
            // Arrange
            using var provider = this.factory!.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            const int totalRecords = 10000;

            // Insert test data
            var products = Enumerable.Range(1, totalRecords).Select(i => new Product
            {
                Id = $"query-perf-{i:D6}",
                Name = $"Query Performance Product {i}",
                Quantity = i % 1000,
                Price = (i % 100) * 9.99m
            }).ToList();

            var entities = products.Select(p => new KeyValuePair<string, Product>(p.Key, p));
            await provider.SaveManyAsync(entities);

            // Warm up
            await provider.GetAllAsync();

            // Act & Assert - Different query scenarios
            var scenarios = new[]
            {
                ("All records", (Expression<Func<Product, bool>>?)null, totalRecords),
                ("Price > 500", (Expression<Func<Product, bool>>)(p => p.Price > 500m), totalRecords * 49 / 100),
                ("Quantity < 100", (Expression<Func<Product, bool>>)(p => p.Quantity < 100), totalRecords * 10 / 100),
                ("High value items", (Expression<Func<Product, bool>>)(p => p.Price > 800m && p.Quantity > 500), totalRecords * 95 / 1000)
            };

            foreach (var (name, predicate, expectedCount) in scenarios)
            {
                var sw = Stopwatch.StartNew();
                var results = await provider.GetAllAsync(predicate);
                var count = results.Count();
                sw.Stop();

                this.output.WriteLine($"{name}: {count} records in {sw.Elapsed.TotalMilliseconds:F2}ms");
                count.Should().BeInRange(expectedCount - (int)(expectedCount * 0.1), expectedCount + (int)(expectedCount * 0.1)); // Within 10%
            }
        }

        [Fact]
        public async Task LargeDataSetPerformance()
        {
            // Arrange
            using var provider = this.factory!.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            const int batchSize = 1000;
            const int batchCount = 10;
            var totalSw = Stopwatch.StartNew();

            // Act - Insert large dataset in batches
            for (int batch = 0; batch < batchCount; batch++)
            {
                var products = Enumerable.Range(batch * batchSize, batchSize).Select(i => new Product
                {
                    Id = $"large-dataset-{i:D8}",
                    Name = $"Large Dataset Product {i}",
                    Description = new string('X', 500), // Large description
                    Quantity = i,
                    Price = i * 0.99m,
                    Tags = Enumerable.Range(0, 10).Select(t => $"tag{t}").ToList()
                }).ToList();

                var entities = products.Select(p => new KeyValuePair<string, Product>(p.Key, p));

                var batchSw = Stopwatch.StartNew();
                await provider.SaveManyAsync(entities);
                batchSw.Stop();

                this.output.WriteLine($"Batch {batch + 1}/{batchCount}: {batchSw.Elapsed.TotalMilliseconds:F2}ms");
            }

            totalSw.Stop();

            // Verify count
            var count = await provider.CountAsync();
            count.Should().Be(batchSize * batchCount);

            // Test query performance on large dataset
            var querySw = Stopwatch.StartNew();
            var expensiveProducts = await provider.GetAllAsync(p => p.Price > 5000m);
            var expensiveCount = expensiveProducts.Count();
            querySw.Stop();

            this.output.WriteLine($"Total insert time: {totalSw.Elapsed.TotalSeconds:F2}s");
            this.output.WriteLine($"Query on {count} records: {querySw.Elapsed.TotalMilliseconds:F2}ms, found {expensiveCount} matches");

            // Assert
            totalSw.Elapsed.TotalSeconds.Should().BeLessThan(30); // Should complete within 30 seconds
            querySw.Elapsed.TotalMilliseconds.Should().BeLessThan(5000); // Query should be fast
        }

        [Fact]
        public async Task ConcurrentPerformance()
        {
            // Arrange
            using var provider = this.factory!.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            const int threadCount = 5;
            const int operationsPerThread = 200;
            var tasks = new Task<double>[threadCount];

            // Act
            var totalSw = Stopwatch.StartNew();

            for (int t = 0; t < threadCount; t++)
            {
                var threadIndex = t;
                tasks[t] = Task.Run(async () =>
                {
                    var threadSw = Stopwatch.StartNew();
                    using var threadProvider = this.factory!.Create<Product>(this.providerName);

                    for (int i = 0; i < operationsPerThread; i++)
                    {
                        var product = new Product
                        {
                            Id = $"concurrent-{threadIndex}-{i:D4}",
                            Name = $"Concurrent Product T{threadIndex} #{i}",
                            Quantity = i
                        };

                        await threadProvider.SaveAsync(product.Key, product);

                        if (i % 2 == 0)
                        {
                            await threadProvider.GetAsync(product.Key);
                        }
                    }

                    threadSw.Stop();
                    return threadSw.Elapsed.TotalMilliseconds;
                });
            }

            var threadTimes = await Task.WhenAll(tasks);
            totalSw.Stop();

            // Output results
            this.output.WriteLine($"Concurrent performance ({threadCount} threads, {operationsPerThread} ops/thread):");
            this.output.WriteLine($"Total time: {totalSw.Elapsed.TotalMilliseconds:F2}ms");
            for (int i = 0; i < threadCount; i++)
            {
                this.output.WriteLine($"  Thread {i}: {threadTimes[i]:F2}ms");
            }

            var avgThreadTime = threadTimes.Average();
            var throughput = (threadCount * operationsPerThread * 1000.0) / totalSw.Elapsed.TotalMilliseconds;
            this.output.WriteLine($"Average thread time: {avgThreadTime:F2}ms");
            this.output.WriteLine($"Throughput: {throughput:F2} ops/sec");

            // Assert - SQLite throughput expectations (more relaxed due to synchronous disk writes)
            throughput.Should().BeGreaterThan(50); // Should handle at least 50 ops/sec with concurrent access
        }
    }
}