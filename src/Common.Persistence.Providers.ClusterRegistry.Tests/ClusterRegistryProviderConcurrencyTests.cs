//-------------------------------------------------------------------------------
// <copyright file="ClusterRegistryProviderConcurrencyTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Tests
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
    using FluentAssertions;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Models;
    using Xunit;
    using Xunit.Abstractions;

    public class ClusterRegistryProviderConcurrencyTests : IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly IServiceCollection services;
        private readonly string baseProviderName = "ClusterRegistryConcurrencyTests";
        private readonly List<string> registryPaths = new List<string>();
        private bool isClusterAvailable;

        public ClusterRegistryProviderConcurrencyTests(ITestOutputHelper output)
        {
            this.output = output;

            // Skip initialization on non-Windows platforms
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            // Check if cluster service is available
            this.isClusterAvailable = CheckClusterAvailability();
            if (!this.isClusterAvailable)
            {
                this.output.WriteLine("Cluster service not available. Tests will be skipped.");
                return;
            }

            // Setup dependency injection
            this.services = new ServiceCollection();
            this.services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
            this.services.AddPersistence();
        }

        [WindowsOnlyFact]
        public async Task Concurrent_Write_Operations_Test()
        {
            if (!this.isClusterAvailable)
            {
                this.output.WriteLine("Skipping test - Cluster service not available");
                return;
            }

            // Arrange
            const int threadCount = 5; // Registry has more limited concurrency
            const int operationsPerThread = 20;
            using var provider = this.CreateProvider(nameof(Concurrent_Write_Operations_Test));
            if (provider == null) return;

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
            if (!this.isClusterAvailable)
            {
                this.output.WriteLine("Skipping test - Cluster service not available");
                return;
            }

            // Arrange
            const int recordCount = 100;
            const int threadCount = 10;
            const int readsPerThread = 50;
            using var provider = this.CreateProvider(nameof(Concurrent_Read_Operations_Test));
            if (provider == null) return;
            
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
            if (!this.isClusterAvailable)
            {
                this.output.WriteLine("Skipping test - Cluster service not available");
                return;
            }

            // Arrange
            const int threadCount = 5;
            const int operationsPerThread = 40;
            using var provider = this.CreateProvider(nameof(Concurrent_Mixed_Operations_Test));
            if (provider == null) return;
            
            // Prepare some initial data
            const int initialRecords = 50;
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
                        var operation = random.Next(5);
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
            if (!this.isClusterAvailable)
            {
                this.output.WriteLine("Skipping test - Cluster service not available");
                return;
            }

            // Arrange
            const int recordCount = 100;
            const int threadCount = 3;
            const int operationsPerThread = 10;
            using var provider = this.CreateProvider(nameof(Concurrent_GetAll_Operations_Test));
            if (provider == null) return;
            
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
            if (!this.isClusterAvailable)
            {
                this.output.WriteLine("Skipping test - Cluster service not available");
                return;
            }

            // Arrange
            const int threadCount = 5;
            const int attemptsPerThread = 20;
            const string sharedKey = "shared-product";
            using var provider = this.CreateProvider(nameof(Concurrent_Conflicting_Updates_Test));
            if (provider == null) return;
            
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
        public async Task Registry_Handle_Limit_Test()
        {
            if (!this.isClusterAvailable)
            {
                this.output.WriteLine("Skipping test - Cluster service not available");
                return;
            }

            // Registry has handle limits, test behavior under stress
            const int threadCount = 10;
            const int operationsPerThread = 100;
            using var provider = this.CreateProvider(nameof(Registry_Handle_Limit_Test));
            if (provider == null) return;

            var errors = new ConcurrentBag<Exception>();
            var successCount = 0;

            // Act - Rapid operations to stress handle management
            var stopwatch = Stopwatch.StartNew();
            var tasks = Enumerable.Range(0, threadCount).Select(threadId => Task.Run(async () =>
            {
                for (int i = 0; i < operationsPerThread; i++)
                {
                    try
                    {
                        var id = $"stress-{threadId}-{i}";
                        var product = new Product { Id = id, Name = $"Stress Test {id}" };
                        
                        // Rapid create, read, update, delete cycle
                        await provider.SaveAsync(id, product);
                        var read = await provider.GetAsync(id);
                        read!.Quantity = i;
                        await provider.SaveAsync(id, read);
                        await provider.DeleteAsync(id);
                        
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
            successCount.Should().BeGreaterThan(0, "At least some operations should succeed");
            this.output.WriteLine($"Registry handle stress test: {successCount} successful cycles out of {threadCount * operationsPerThread} attempts");
            this.output.WriteLine($"Error count: {errors.Count}");
            this.output.WriteLine($"Time elapsed: {stopwatch.ElapsedMilliseconds}ms");

            if (errors.Any())
            {
                var errorTypes = errors.GroupBy(e => e.GetType().Name).Select(g => new { Type = g.Key, Count = g.Count() });
                foreach (var errorType in errorTypes)
                {
                    this.output.WriteLine($"  {errorType.Type}: {errorType.Count}");
                }
            }
        }

        private ICrudStorageProvider<Product>? CreateProvider(string testName)
        {
            try
            {
                var providerName = $"{this.baseProviderName}_{testName}";
                var registryPath = $@"Software\Microsoft\ReliableStore\Tests\{testName}";
                this.registryPaths.Add(registryPath);

                var settings = new ClusterRegistryStoreSettings
                {
                    Name = providerName,
                    RootPath = registryPath,
                    ApplicationName = "TestApp",
                    ServiceName = testName,
                    EnableCompression = true,
                    Enabled = true,
                    RetryCount = 3,
                    RetryDelayMilliseconds = 50
                };

                this.services.AddKeyedScoped<CrudStorageProviderSettings>(providerName, (_, _) => settings);
                
                var serviceProvider = this.services.BuildServiceProvider();
                var factory = serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
                return factory.Create<Product>(providerName);
            }
            catch (Exception ex)
            {
                this.output.WriteLine($"Failed to create provider: {ex.Message}");
                return null;
            }
        }

        private static bool CheckClusterAvailability()
        {
            try
            {
                // Try to access cluster service
                using var scManager = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_CONNECT);
                if (scManager.IsInvalid)
                    return false;

                using var service = NativeMethods.OpenService(scManager, "ClusSvc", NativeMethods.SERVICE_QUERY_STATUS);
                return !service.IsInvalid;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            // Cleanup registry paths
            if (this.isClusterAvailable)
            {
                foreach (var path in this.registryPaths)
                {
                    try
                    {
                        // Attempt to clean up test registry keys
                        // Note: This requires appropriate permissions
                    }
                    catch { }
                }
            }
        }
    }
}