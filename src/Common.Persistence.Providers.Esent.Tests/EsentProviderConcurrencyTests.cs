//-------------------------------------------------------------------------------
// <copyright file="EsentProviderConcurrencyTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.Esent.Tests
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Persistence.Configuration;
    using Common.Persistence.Contract;
    using Common.Persistence.Factory;
    using AwesomeAssertions;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Models;
    using Xunit.Abstractions;

    public class EsentProviderConcurrencyTests : IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly IServiceCollection services;
        private readonly string providerName = "EsentProviderConcurrencyTests";
        private readonly List<string> tempDatabases = new List<string>();

        public EsentProviderConcurrencyTests(ITestOutputHelper output)
        {
            this.output = output;

            // Skip initialization on non-Windows platforms
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            // Setup dependency injection
            this.services = new ServiceCollection();
            this.services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
            var configuration = this.services.AddConfiguration();
            var settings = configuration.GetConfiguredSettings<EsentStoreSettings>(this.providerName);
            this.services.AddKeyedScoped<CrudStorageProviderSettings>(this.providerName, (_, _) => settings);
            this.services.AddPersistence();
        }

        [WindowsOnlyFact]
        public async Task Concurrent_Write_Operations_Test()
        {
            // Arrange
            const int threadCount = 10;
            const int operationsPerThread = 100;
            using var provider = this.CreateProvider(nameof(Concurrent_Write_Operations_Test), useSessionPool: true);
            var errors = new ConcurrentBag<Exception>();
            var successCount = 0;

            // Act
            var stopwatch = Stopwatch.StartNew();
            var tasks = Enumerable.Range(0, threadCount).Select(threadId => Task.Run(async () =>
            {
                for (int i = 0; i < operationsPerThread; i++)
                {
                    try
                    {
                        var product = new Product
                        {
                            Id = $"product-{threadId}-{i}",
                            Name = $"Product from thread {threadId}, item {i}",
                            Quantity = threadId * 100 + i,
                            Price = (threadId + 1) * (i + 1) * 10.0m
                        };
                        await provider.SaveAsync(product.Id, product);
                        Interlocked.Increment(ref successCount);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }
            })).ToArray();

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            errors.Should().BeEmpty("All concurrent writes should succeed");
            successCount.Should().Be(threadCount * operationsPerThread);
            
            var totalCount = await provider.CountAsync();
            totalCount.Should().Be(threadCount * operationsPerThread);

            this.output.WriteLine($"Concurrent writes: {threadCount} threads × {operationsPerThread} operations = {successCount} successful writes in {stopwatch.ElapsedMilliseconds}ms");
            this.output.WriteLine($"Throughput: {successCount / stopwatch.Elapsed.TotalSeconds:F2} writes/second");
        }

        [WindowsOnlyFact]
        public async Task Concurrent_Read_Operations_Test()
        {
            // Arrange
            const int recordCount = 1000;
            const int threadCount = 20;
            const int readsPerThread = 100;
            using var provider = this.CreateProvider(nameof(Concurrent_Read_Operations_Test), useSessionPool: true);
            
            // Prepare test data
            var products = Enumerable.Range(1, recordCount).Select(i => new Product
            {
                Id = $"product-{i}",
                Name = $"Product {i}",
                Quantity = i,
                Price = i * 10.0m
            }).ToList();

            foreach (var product in products)
            {
                await provider.SaveAsync(product.Id, product);
            }

            var errors = new ConcurrentBag<Exception>();
            var successCount = 0;
            var random = new Random(42);

            // Act
            var stopwatch = Stopwatch.StartNew();
            var tasks = Enumerable.Range(0, threadCount).Select(threadId => Task.Run(async () =>
            {
                var localRandom = new Random(threadId);
                for (int i = 0; i < readsPerThread; i++)
                {
                    try
                    {
                        var productId = $"product-{localRandom.Next(1, recordCount + 1)}";
                        var result = await provider.GetAsync(productId);
                        result.Should().NotBeNull();
                        Interlocked.Increment(ref successCount);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }
            })).ToArray();

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            errors.Should().BeEmpty("All concurrent reads should succeed");
            successCount.Should().Be(threadCount * readsPerThread);

            this.output.WriteLine($"Concurrent reads: {threadCount} threads × {readsPerThread} operations = {successCount} successful reads in {stopwatch.ElapsedMilliseconds}ms");
            this.output.WriteLine($"Throughput: {successCount / stopwatch.Elapsed.TotalSeconds:F2} reads/second");
        }

        [WindowsOnlyFact]
        public async Task Concurrent_Mixed_Operations_Test()
        {
            // Arrange
            const int threadCount = 10;
            const int operationsPerThread = 100;
            using var provider = this.CreateProvider(nameof(Concurrent_Mixed_Operations_Test), useSessionPool: true);
            
            // Prepare some initial data
            const int initialRecords = 500;
            for (int i = 0; i < initialRecords; i++)
            {
                await provider.SaveAsync($"initial-{i}", new Product { Id = $"initial-{i}", Name = $"Initial Product {i}", Quantity = i });
            }

            var errors = new ConcurrentBag<Exception>();
            var operationCounts = new ConcurrentDictionary<string, int>();

            // Act
            var stopwatch = Stopwatch.StartNew();
            var tasks = Enumerable.Range(0, threadCount).Select(threadId => Task.Run(async () =>
            {
                var random = new Random(threadId);
                for (int i = 0; i < operationsPerThread; i++)
                {
                    try
                    {
                        var operation = random.Next(5); // 0-4
                        switch (operation)
                        {
                            case 0: // Create
                                var newProduct = new Product
                                {
                                    Id = $"new-{threadId}-{i}",
                                    Name = $"New Product {threadId}-{i}",
                                    Quantity = random.Next(100),
                                    Price = random.Next(1000) * 0.99m
                                };
                                await provider.SaveAsync(newProduct.Id, newProduct);
                                operationCounts.AddOrUpdate("Create", 1, (_, v) => v + 1);
                                break;

                            case 1: // Read
                                var readId = random.Next(2) == 0 ? $"initial-{random.Next(initialRecords)}" : $"new-{random.Next(threadCount)}-{random.Next(i + 1)}";
                                var readResult = await provider.GetAsync(readId);
                                operationCounts.AddOrUpdate("Read", 1, (_, v) => v + 1);
                                break;

                            case 2: // Update
                                var updateId = $"initial-{random.Next(initialRecords)}";
                                var existing = await provider.GetAsync(updateId);
                                if (existing != null)
                                {
                                    existing.Quantity = random.Next(1000);
                                    existing.Price = random.Next(10000) * 0.99m;
                                    await provider.SaveAsync(updateId, existing);
                                    operationCounts.AddOrUpdate("Update", 1, (_, v) => v + 1);
                                }
                                break;

                            case 3: // Delete
                                var deleteId = $"initial-{random.Next(initialRecords)}";
                                await provider.DeleteAsync(deleteId);
                                operationCounts.AddOrUpdate("Delete", 1, (_, v) => v + 1);
                                break;

                            case 4: // Exists check
                                var checkId = random.Next(2) == 0 ? $"initial-{random.Next(initialRecords)}" : $"nonexistent-{random.Next(1000)}";
                                await provider.ExistsAsync(checkId);
                                operationCounts.AddOrUpdate("Exists", 1, (_, v) => v + 1);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }
            })).ToArray();

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            errors.Should().BeEmpty("All concurrent mixed operations should succeed");
            var totalOperations = operationCounts.Values.Sum();
            totalOperations.Should().Be(threadCount * operationsPerThread);

            this.output.WriteLine($"Concurrent mixed operations completed in {stopwatch.ElapsedMilliseconds}ms");
            this.output.WriteLine($"Total throughput: {totalOperations / stopwatch.Elapsed.TotalSeconds:F2} operations/second");
            foreach (var kvp in operationCounts.OrderBy(k => k.Key))
            {
                this.output.WriteLine($"  {kvp.Key}: {kvp.Value} operations");
            }
        }

        [WindowsOnlyFact]
        public async Task Concurrent_GetAll_Operations_Test()
        {
            // Arrange
            const int recordCount = 1000;
            const int threadCount = 5;
            const int operationsPerThread = 20;
            using var provider = this.CreateProvider(nameof(Concurrent_GetAll_Operations_Test), useSessionPool: true);
            
            // Prepare test data
            for (int i = 0; i < recordCount; i++)
            {
                await provider.SaveAsync($"product-{i}", new Product { Id = $"product-{i}", Name = $"Product {i}", Quantity = i % 100 });
            }

            var errors = new ConcurrentBag<Exception>();
            var successCount = 0;

            // Act
            var stopwatch = Stopwatch.StartNew();
            var tasks = Enumerable.Range(0, threadCount).Select(threadId => Task.Run(async () =>
            {
                for (int i = 0; i < operationsPerThread; i++)
                {
                    try
                    {
                        // Mix GetAll with and without predicate
                        if (i % 2 == 0)
                        {
                            var all = await provider.GetAllAsync();
                            all.Count().Should().Be(recordCount);
                        }
                        else
                        {
                            var filtered = await provider.GetAllAsync(p => p.Quantity > 50);
                            filtered.Count().Should().BeGreaterThan(0).And.BeLessThan(recordCount);
                        }
                        Interlocked.Increment(ref successCount);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }
            })).ToArray();

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            errors.Should().BeEmpty("All concurrent GetAll operations should succeed");
            successCount.Should().Be(threadCount * operationsPerThread);

            this.output.WriteLine($"Concurrent GetAll operations: {successCount} successful in {stopwatch.ElapsedMilliseconds}ms");
            this.output.WriteLine($"Throughput: {successCount / stopwatch.Elapsed.TotalSeconds:F2} operations/second");
        }

        [WindowsOnlyFact]
        public async Task Concurrent_Conflicting_Updates_Test()
        {
            // Arrange
            const int threadCount = 10;
            const int attemptsPerThread = 50;
            const string sharedKey = "shared-product";
            using var provider = this.CreateProvider(nameof(Concurrent_Conflicting_Updates_Test), useSessionPool: true);
            
            // Create initial product
            var initialProduct = new Product { Id = sharedKey, Name = "Shared Product", Quantity = 0, Price = 100.0m };
            await provider.SaveAsync(sharedKey, initialProduct);

            var updateCounts = new ConcurrentBag<int>();

            // Act - Multiple threads trying to update the same record
            var stopwatch = Stopwatch.StartNew();
            var tasks = Enumerable.Range(0, threadCount).Select(threadId => Task.Run(async () =>
            {
                var localUpdateCount = 0;
                for (int i = 0; i < attemptsPerThread; i++)
                {
                    try
                    {
                        var product = await provider.GetAsync(sharedKey);
                        product!.Quantity++;
                        product.Price += 1.0m;
                        await provider.SaveAsync(sharedKey, product);
                        localUpdateCount++;
                    }
                    catch
                    {
                        // Expected - some updates may fail due to conflicts
                    }
                }
                updateCounts.Add(localUpdateCount);
            })).ToArray();

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            var totalUpdates = updateCounts.Sum();
            totalUpdates.Should().BeGreaterThan(0, "At least some updates should succeed");
            
            var finalProduct = await provider.GetAsync(sharedKey);
            finalProduct.Should().NotBeNull();

            this.output.WriteLine($"Concurrent conflicting updates: {totalUpdates} successful out of {threadCount * attemptsPerThread} attempts in {stopwatch.ElapsedMilliseconds}ms");
            this.output.WriteLine($"Final quantity: {finalProduct!.Quantity}, Final price: {finalProduct.Price:C}");
            this.output.WriteLine($"Success rate: {(double)totalUpdates / (threadCount * attemptsPerThread):P}");
        }

        [WindowsOnlyFact]
        public async Task SessionPool_Concurrency_Test()
        {
            // Compare concurrency performance with and without session pool
            const int threadCount = 20;
            const int operationsPerThread = 50;

            // Test without session pool
            using (var providerNoPool = this.CreateProvider(nameof(SessionPool_Concurrency_Test) + "_NoPool", useSessionPool: false))
            {
                var stopwatchNoPool = Stopwatch.StartNew();
                var tasksNoPool = Enumerable.Range(0, threadCount).Select(threadId => Task.Run(async () =>
                {
                    for (int i = 0; i < operationsPerThread; i++)
                    {
                        var id = $"product-{threadId}-{i}";
                        await providerNoPool.SaveAsync(id, new Product { Id = id, Name = $"Product {id}" });
                        await providerNoPool.GetAsync(id);
                    }
                })).ToArray();

                await Task.WhenAll(tasksNoPool);
                stopwatchNoPool.Stop();
                this.output.WriteLine($"Without session pool: {threadCount} threads × {operationsPerThread} operations in {stopwatchNoPool.ElapsedMilliseconds}ms");
            }

            // Test with session pool
            using (var providerWithPool = this.CreateProvider(nameof(SessionPool_Concurrency_Test) + "_Pool", useSessionPool: true))
            {
                var stopwatchPool = Stopwatch.StartNew();
                var tasksPool = Enumerable.Range(0, threadCount).Select(threadId => Task.Run(async () =>
                {
                    for (int i = 0; i < operationsPerThread; i++)
                    {
                        var id = $"product-{threadId}-{i}";
                        await providerWithPool.SaveAsync(id, new Product { Id = id, Name = $"Product {id}" });
                        await providerWithPool.GetAsync(id);
                    }
                })).ToArray();

                await Task.WhenAll(tasksPool);
                stopwatchPool.Stop();
                this.output.WriteLine($"With session pool: {threadCount} threads × {operationsPerThread} operations in {stopwatchPool.ElapsedMilliseconds}ms");
            }
        }

        private ICrudStorageProvider<Product> CreateProvider(string testName, bool useSessionPool = false)
        {
            var dbPath = $"data/test_{testName}.db";
            this.tempDatabases.Add(dbPath);

            var settings = new EsentStoreSettings
            {
                Name = this.providerName,
                DatabasePath = dbPath,
                InstanceName = $"TestInstance_{testName}",
                UseSessionPool = useSessionPool,
                Enabled = true
            };

            this.services.AddKeyedScoped<CrudStorageProviderSettings>(this.providerName, (_, _) => settings);
            
            var serviceProvider = this.services.BuildServiceProvider();
            var factory = serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            return factory.Create<Product>(providerName);
        }

        public void Dispose()
        {
            // Cleanup temp databases
            foreach (var dbPath in this.tempDatabases)
            {
                try
                {
                    if (System.IO.File.Exists(dbPath))
                    {
                        System.IO.File.Delete(dbPath);
                    }
                    
                    // Also clean up ESENT log files
                    var directory = System.IO.Path.GetDirectoryName(dbPath);
                    if (System.IO.Directory.Exists(directory))
                    {
                        foreach (var file in System.IO.Directory.GetFiles(directory, "edb*.log"))
                        {
                            try { System.IO.File.Delete(file); } catch { }
                        }
                        foreach (var file in System.IO.Directory.GetFiles(directory, "*.chk"))
                        {
                            try { System.IO.File.Delete(file); } catch { }
                        }
                    }
                }
                catch { }
            }
        }
    }
}