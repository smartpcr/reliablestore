//-------------------------------------------------------------------------------
// <copyright file="InMemoryProviderConcurrencyTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.InMemory.Tests
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Persistence.Configuration;
    using Common.Persistence.Contract;
    using Common.Persistence.Factory;
    using AwesomeAssertions;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Models;
    using Xunit;
    using Xunit.Abstractions;

    public class InMemoryProviderConcurrencyTests : IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly IServiceProvider serviceProvider;
        private readonly string providerName = "InMemoryConcTest";

        public InMemoryProviderConcurrencyTests(ITestOutputHelper output)
        {
            this.output = output;

            // Setup DI container
            var services = new ServiceCollection();
            services.AddLogging(builder => builder
                .SetMinimumLevel(LogLevel.Warning)); // Less logging for concurrency tests

            // Configure InMemory provider
            var config = new Dictionary<string, string>
            {
                [$"Providers:{this.providerName}:Name"] = this.providerName,
                [$"Providers:{this.providerName}:Enabled"] = "true",
                [$"Providers:{this.providerName}:MaxCacheSize"] = "100000", // 100k for concurrency tests
                [$"Providers:{this.providerName}:EnableEviction"] = "false" // Disable eviction for tests
            };

            var configuration = services.AddConfiguration(config);
            
            // Register settings
            var settings = configuration.GetConfiguredSettings<InMemoryStoreSettings>($"Providers:{this.providerName}");
            services.AddKeyedSingleton<CrudStorageProviderSettings>(this.providerName, (_, _) => settings);
            
            services.AddPersistence();

            this.serviceProvider = services.BuildServiceProvider();
        }

        public void Dispose()
        {
            if (this.serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        [Fact]
        public async Task Concurrent_Create_Same_Key_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int threadCount = 100;
            const string sharedKey = "shared-product-key";
            var barrier = new Barrier(threadCount);
            var successCount = 0;
            var errors = new ConcurrentBag<Exception>();

            // Act
            var tasks = Enumerable.Range(0, threadCount).Select(i => Task.Run(async () =>
            {
                try
                {
                    // Synchronize all threads to start at the same time
                    barrier.SignalAndWait();
                    
                    var product = new Product
                    {
                        Id = sharedKey,
                        Name = $"Product from thread {i}",
                        Price = i * 10m,
                        Quantity = i
                    };
                    
                    await provider.SaveAsync(sharedKey, product);
                    Interlocked.Increment(ref successCount);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            // Assert
            successCount.Should().Be(threadCount, "All threads should successfully save (last write wins)");
            errors.Should().BeEmpty("No errors should occur with proper locking");
            
            var finalProduct = await provider.GetAsync(sharedKey);
            finalProduct.Should().NotBeNull();
            this.output.WriteLine($"Final product saved by thread with price: {finalProduct!.Price}");
        }

        [Fact]
        public async Task Concurrent_Update_Same_Entity_Atomic_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int threadCount = 100;
            const int incrementsPerThread = 1000;
            const string productId = "counter-product";
            
            // Create initial product
            var initialProduct = new Product
            {
                Id = productId,
                Name = "Counter Product",
                Quantity = 0,
                Price = 0m
            };
            await provider.SaveAsync(productId, initialProduct);

            var barrier = new Barrier(threadCount);
            var errors = new ConcurrentBag<Exception>();

            // Act - Concurrent increments
            var tasks = Enumerable.Range(0, threadCount).Select(threadIndex => Task.Run(async () =>
            {
                try
                {
                    barrier.SignalAndWait();
                    
                    for (int i = 0; i < incrementsPerThread; i++)
                    {
                        // Read-modify-write pattern (not atomic, will have race conditions)
                        var product = await provider.GetAsync(productId);
                        product!.Quantity++;
                        product.Price += 1m;
                        await provider.SaveAsync(productId, product);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            })).ToArray();

            var sw = Stopwatch.StartNew();
            await Task.WhenAll(tasks);
            sw.Stop();

            // Assert
            errors.Should().BeEmpty("No errors should occur during concurrent updates");
            
            var finalProduct = await provider.GetAsync(productId);
            finalProduct.Should().NotBeNull();
            
            // Due to race conditions in read-modify-write, we won't have perfect increments
            finalProduct!.Quantity.Should().BeGreaterThan(0);
            finalProduct.Price.Should().BeGreaterThan(0m);
            
            var expectedTotal = threadCount * incrementsPerThread;
            var lostUpdates = expectedTotal - finalProduct.Quantity;
            var lostPercentage = (lostUpdates / (double)expectedTotal) * 100;
            
            this.output.WriteLine($"Final quantity: {finalProduct.Quantity} (expected: {expectedTotal})");
            this.output.WriteLine($"Lost updates: {lostUpdates} ({lostPercentage:F1}%)");
            this.output.WriteLine($"Time: {sw.ElapsedMilliseconds}ms");
            this.output.WriteLine($"Throughput: {(threadCount * incrementsPerThread) / sw.Elapsed.TotalSeconds:F0} ops/sec");
        }

        [Fact]
        public async Task Concurrent_Different_Keys_Perfect_Isolation_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int threadCount = 50;
            const int operationsPerThread = 1000;
            var errors = new ConcurrentBag<Exception>();
            var completedThreads = 0;

            // Act
            var sw = Stopwatch.StartNew();
            var tasks = Enumerable.Range(0, threadCount).Select(threadIndex => Task.Run(async () =>
            {
                try
                {
                    var threadKey = $"thread-{threadIndex}-product";
                    
                    for (int i = 0; i < operationsPerThread; i++)
                    {
                        var product = new Product
                        {
                            Id = threadKey,
                            Name = $"Product for thread {threadIndex}",
                            Quantity = i,
                            Price = threadIndex * 10m + i
                        };
                        
                        await provider.SaveAsync(threadKey, product);
                        
                        var retrieved = await provider.GetAsync(threadKey);
                        retrieved.Should().NotBeNull();
                        retrieved!.Quantity.Should().Be(i);
                    }
                    
                    Interlocked.Increment(ref completedThreads);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            })).ToArray();

            await Task.WhenAll(tasks);
            sw.Stop();

            // Assert
            errors.Should().BeEmpty("No errors should occur when threads work on different keys");
            completedThreads.Should().Be(threadCount, "All threads should complete successfully");
            
            // Verify final state
            for (int i = 0; i < threadCount; i++)
            {
                var product = await provider.GetAsync($"thread-{i}-product");
                product.Should().NotBeNull();
                product!.Quantity.Should().Be(operationsPerThread - 1);
            }
            
            var totalOps = threadCount * operationsPerThread * 2; // Save + Get
            var throughput = totalOps / sw.Elapsed.TotalSeconds;
            this.output.WriteLine($"Total operations: {totalOps} in {sw.ElapsedMilliseconds}ms");
            this.output.WriteLine($"Throughput: {throughput:F0} ops/sec");
        }

        [Fact]
        public async Task Concurrent_Read_Write_Delete_Stress_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int entityCount = 1000;
            const int threadCount = 20;
            const int operationsPerThread = 5000;
            
            // Pre-populate data
            var products = Enumerable.Range(1, entityCount).Select(i => new Product
            {
                Id = $"rwd-product-{i}",
                Name = $"RWD Product {i}",
                Quantity = i,
                Price = i * 10m
            }).ToList();
            
            foreach (var product in products)
            {
                await provider.SaveAsync(product.Id, product);
            }

            var errors = new ConcurrentBag<Exception>();
            var operationCounts = new ConcurrentDictionary<string, int>();

            // Act
            var sw = Stopwatch.StartNew();
            var tasks = Enumerable.Range(0, threadCount).Select(threadIndex => Task.Run(async () =>
            {
                var localRandom = new Random(threadIndex);
                
                for (int i = 0; i < operationsPerThread; i++)
                {
                    try
                    {
                        var index = localRandom.Next(entityCount);
                        var productId = $"rwd-product-{index + 1}";
                        var operation = localRandom.Next(3);
                        
                        switch (operation)
                        {
                            case 0: // Read
                                var product = await provider.GetAsync(productId);
                                operationCounts.AddOrUpdate("read", 1, (_, count) => count + 1);
                                if (product != null)
                                {
                                    operationCounts.AddOrUpdate("read-found", 1, (_, count) => count + 1);
                                }
                                break;
                                
                            case 1: // Write
                                await provider.SaveAsync(productId, new Product
                                {
                                    Id = productId,
                                    Name = $"Updated by thread {threadIndex}",
                                    Quantity = localRandom.Next(1000),
                                    Price = localRandom.Next(1, 1000)
                                });
                                operationCounts.AddOrUpdate("write", 1, (_, count) => count + 1);
                                break;
                                
                            case 2: // Delete
                                await provider.DeleteAsync(productId);
                                operationCounts.AddOrUpdate("delete", 1, (_, count) => count + 1);
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
            sw.Stop();

            // Assert
            errors.Should().BeEmpty("No errors should occur during concurrent operations");
            
            var totalOperations = operationCounts.Values.Sum();
            var throughput = totalOperations / sw.Elapsed.TotalSeconds;
            
            this.output.WriteLine($"Total operations: {totalOperations} in {sw.ElapsedMilliseconds}ms");
            this.output.WriteLine($"Throughput: {throughput:F0} ops/sec");
            this.output.WriteLine($"Operation counts:");
            foreach (var kvp in operationCounts.OrderBy(x => x.Key))
            {
                this.output.WriteLine($"  {kvp.Key}: {kvp.Value}");
            }
        }

        [Fact]
        public async Task Concurrent_GetAll_While_Modifying_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int writerThreads = 10;
            const int readerThreads = 10;
            const int writesPerThread = 1000;
            
            var stopReading = false;
            var errors = new ConcurrentBag<Exception>();
            var readCounts = new ConcurrentBag<int>();
            var getAllSizes = new ConcurrentBag<int>();

            // Writer tasks
            var writerTasks = Enumerable.Range(0, writerThreads).Select(threadIndex => Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < writesPerThread; i++)
                    {
                        var product = new Product
                        {
                            Id = $"getall-{threadIndex:D2}-{i:D4}",
                            Name = $"Product {threadIndex}-{i}",
                            Price = threadIndex * 100m + i,
                            Quantity = i
                        };
                        
                        await provider.SaveAsync(product.Id, product);
                        
                        // Occasionally delete some
                        if (i % 10 == 0 && i > 0)
                        {
                            await provider.DeleteAsync($"getall-{threadIndex:D2}-{i - 10:D4}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            })).ToArray();

            // Reader tasks
            var readerTasks = Enumerable.Range(0, readerThreads).Select(_ => Task.Run(async () =>
            {
                try
                {
                    var localReadCount = 0;
                    while (!stopReading)
                    {
                        var allProducts = await provider.GetAllAsync();
                        var count = allProducts.Count();
                        getAllSizes.Add(count);
                        localReadCount++;
                        
                        // Also do filtered queries
                        if (localReadCount % 5 == 0)
                        {
                            var filtered = await provider.GetAllAsync(p => p.Price > 500);
                            getAllSizes.Add(filtered.Count());
                        }
                    }
                    readCounts.Add(localReadCount);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            })).ToArray();

            // Act
            await Task.WhenAll(writerTasks);
            stopReading = true;
            await Task.WhenAll(readerTasks);

            // Assert
            errors.Should().BeEmpty("No errors should occur during concurrent GetAll and modifications");
            
            var finalCount = await provider.CountAsync();
            var totalReads = readCounts.Sum();
            var minSize = getAllSizes.Count > 0 ? getAllSizes.Min() : 0;
            var maxSize = getAllSizes.Count > 0 ? getAllSizes.Max() : 0;
            
            totalReads.Should().BeGreaterThan(0, "Readers should have performed some reads");
            
            this.output.WriteLine($"Final entity count: {finalCount}");
            this.output.WriteLine($"Total GetAll operations: {totalReads}");
            this.output.WriteLine($"GetAll sizes ranged from {minSize} to {maxSize}");
            this.output.WriteLine($"Average reads per reader thread: {totalReads / (double)readerThreads:F1}");
        }

        [Fact]
        public async Task High_Contention_Single_Key_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int threadCount = 100;
            const int operationsPerThread = 100;
            const string hotKey = "hot-product";
            
            var errors = new ConcurrentBag<Exception>();
            var operationLatencies = new ConcurrentBag<double>();

            // Act
            var sw = Stopwatch.StartNew();
            var tasks = Enumerable.Range(0, threadCount).Select(threadIndex => Task.Run(async () =>
            {
                for (int i = 0; i < operationsPerThread; i++)
                {
                    try
                    {
                        var opSw = Stopwatch.StartNew();
                        
                        // Alternating read and write
                        if (i % 2 == 0)
                        {
                            var product = await provider.GetAsync(hotKey);
                            if (product == null)
                            {
                                await provider.SaveAsync(hotKey, new Product
                                {
                                    Id = hotKey,
                                    Name = $"Created by thread {threadIndex}",
                                    Quantity = 1,
                                    Price = 100m
                                });
                            }
                        }
                        else
                        {
                            await provider.SaveAsync(hotKey, new Product
                            {
                                Id = hotKey,
                                Name = $"Updated by thread {threadIndex}",
                                Quantity = threadIndex * 100 + i,
                                Price = threadIndex + i
                            });
                        }
                        
                        opSw.Stop();
                        operationLatencies.Add(opSw.Elapsed.TotalMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }
            })).ToArray();

            await Task.WhenAll(tasks);
            sw.Stop();

            // Assert
            errors.Should().BeEmpty("No errors should occur even under high contention");
            
            var totalOps = threadCount * operationsPerThread;
            var throughput = totalOps / sw.Elapsed.TotalSeconds;
            var avgLatency = operationLatencies.Count > 0 ? operationLatencies.Average() : 0;
            var maxLatency = operationLatencies.Count > 0 ? operationLatencies.Max() : 0;
            
            this.output.WriteLine($"High contention test completed in {sw.ElapsedMilliseconds}ms");
            this.output.WriteLine($"Total operations: {totalOps}");
            this.output.WriteLine($"Throughput: {throughput:F0} ops/sec");
            this.output.WriteLine($"Average latency: {avgLatency:F2}ms");
            this.output.WriteLine($"Max latency: {maxLatency:F2}ms");
            
            // Even under high contention, InMemory should be fast
            avgLatency.Should().BeLessThan(10, "Average latency should be under 10ms");
        }
    }
}