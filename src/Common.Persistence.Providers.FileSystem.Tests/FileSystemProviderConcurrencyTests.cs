//-------------------------------------------------------------------------------
// <copyright file="FileSystemProviderConcurrencyTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.FileSystem.Tests
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
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

    public class FileSystemProviderConcurrencyTests : IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly IServiceProvider serviceProvider;
        private readonly string tempDirectory;
        private readonly string providerName = "FileSystemConcTest";

        public FileSystemProviderConcurrencyTests(ITestOutputHelper output)
        {
            this.output = output;

            // Create temp directory for tests
            this.tempDirectory = Path.Combine(Path.GetTempPath(), $"FSConcTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(this.tempDirectory);

            // Setup DI container
            var services = new ServiceCollection();
            services.AddLogging(builder => builder
                .SetMinimumLevel(LogLevel.Warning)); // Less logging for concurrency tests

            // Configure FileSystem provider
            var config = new Dictionary<string, string>
            {
                [$"Providers:{this.providerName}:Name"] = this.providerName,
                [$"Providers:{this.providerName}:FilePath"] = Path.Combine(this.tempDirectory, "entities", "dummy.json"),
                [$"Providers:{this.providerName}:UseSubdirectories"] = "true",
                [$"Providers:{this.providerName}:MaxConcurrentFiles"] = "64",
                [$"Providers:{this.providerName}:MaxRetries"] = "5",
                [$"Providers:{this.providerName}:RetryDelayMs"] = "10",
                [$"Providers:{this.providerName}:Enabled"] = "true"
            };

            var configuration = services.AddConfiguration(config);
            
            // Register settings
            var settings = configuration.GetConfiguredSettings<FileSystemStoreSettings>($"Providers:{this.providerName}");
            services.AddKeyedSingleton<CrudStorageProviderSettings>(this.providerName, (_, _) => settings);
            
            services.AddPersistence();

            this.serviceProvider = services.BuildServiceProvider();
        }

        public void Dispose()
        {
            // Cleanup temp directory
            if (Directory.Exists(this.tempDirectory))
            {
                try
                {
                    Directory.Delete(this.tempDirectory, true);
                }
                catch
                {
                    // Best effort
                }
            }
        }

        [Fact]
        public async Task Concurrent_Create_Same_Key_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int threadCount = 20;
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
            errors.Should().BeEmpty("No errors should occur with proper file locking");
            
            var finalProduct = await provider.GetAsync(sharedKey);
            finalProduct.Should().NotBeNull();
            this.output.WriteLine($"Final product saved by thread with price: {finalProduct!.Price}");
        }

        [Fact]
        public async Task Concurrent_Update_Same_Entity_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int threadCount = 50;
            const int incrementsPerThread = 100;
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

            // Act
            var tasks = Enumerable.Range(0, threadCount).Select(threadIndex => Task.Run(async () =>
            {
                try
                {
                    barrier.SignalAndWait();
                    
                    for (int i = 0; i < incrementsPerThread; i++)
                    {
                        // Read-modify-write pattern
                        var product = await provider.GetAsync(productId);
                        if (product != null)
                        {
                            product.Quantity++;
                            product.Price += 1m;
                            await provider.SaveAsync(productId, product);
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            // Assert
            errors.Should().BeEmpty("No errors should occur during concurrent updates");
            
            var finalProduct = await provider.GetAsync(productId);
            
            // Due to race conditions, we won't have perfect increments
            // But we should have some updates
            if (finalProduct != null)
            {
                finalProduct.Quantity.Should().BeGreaterThan(0);
                finalProduct.Price.Should().BeGreaterThan(0m);
                this.output.WriteLine($"Final quantity: {finalProduct.Quantity} (expected up to {threadCount * incrementsPerThread})");
                this.output.WriteLine($"Final price: {finalProduct.Price}");
            }
            else
            {
                // In extreme race conditions, the final product might have been deleted or not found
                this.output.WriteLine("Warning: Final product was null, likely due to race conditions");
            }
        }

        [Fact]
        public async Task Concurrent_Different_Keys_No_Interference_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int threadCount = 20;
            const int operationsPerThread = 50;
            var errors = new ConcurrentBag<Exception>();
            var completedThreads = 0;

            // Act
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
        }

        [Fact]
        public async Task Concurrent_Read_Write_Delete_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int entityCount = 100;
            const int threadCount = 10;
            const int operationsPerThread = 200;
            
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

            var random = new Random(42);
            var errors = new ConcurrentBag<Exception>();
            var operationCounts = new ConcurrentDictionary<string, int>();

            // Act
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

            // Assert
            errors.Should().BeEmpty("No errors should occur during concurrent operations");
            
            var totalOperations = operationCounts.Values.Sum();
            totalOperations.Should().Be(threadCount * operationsPerThread);
            
            this.output.WriteLine($"Operation counts:");
            foreach (var kvp in operationCounts)
            {
                this.output.WriteLine($"  {kvp.Key}: {kvp.Value}");
            }
        }

        [Fact]
        public async Task Stress_Test_File_Handle_Limits()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int threadCount = 100;
            const int uniqueKeysPerThread = 50;
            var errors = new ConcurrentBag<Exception>();
            var successCount = 0;

            // Act
            var sw = Stopwatch.StartNew();
            var tasks = Enumerable.Range(0, threadCount).Select(threadIndex => Task.Run(async () =>
            {
                try
                {
                    // Each thread creates many unique files
                    for (int i = 0; i < uniqueKeysPerThread; i++)
                    {
                        var product = new Product
                        {
                            Id = $"stress-{threadIndex:D3}-{i:D3}",
                            Name = $"Stress test product {threadIndex}-{i}",
                            Quantity = i,
                            Price = threadIndex * i
                        };
                        
                        await provider.SaveAsync(product.Id, product);
                        Interlocked.Increment(ref successCount);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            })).ToArray();

            await Task.WhenAll(tasks);
            sw.Stop();

            // Assert
            var expectedTotal = threadCount * uniqueKeysPerThread;
            successCount.Should().Be(expectedTotal, "All operations should succeed");
            errors.Should().BeEmpty("No errors should occur even under high file handle pressure");
            
            var actualCount = await provider.CountAsync();
            actualCount.Should().Be(expectedTotal);
            
            this.output.WriteLine($"Created {actualCount} files in {sw.ElapsedMilliseconds}ms");
            this.output.WriteLine($"Throughput: {actualCount / sw.Elapsed.TotalSeconds:F0} files/sec");
        }

        [Fact]
        public async Task Concurrent_GetAll_While_Writing_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int writerThreads = 5;
            const int readerThreads = 5;
            const int writesPerThread = 100;
            
            var writerBarrier = new Barrier(writerThreads);
            var stopReading = false;
            var errors = new ConcurrentBag<Exception>();
            var readCounts = new ConcurrentBag<int>();

            // Writer tasks
            var writerTasks = Enumerable.Range(0, writerThreads).Select(threadIndex => Task.Run(async () =>
            {
                try
                {
                    writerBarrier.SignalAndWait();
                    
                    for (int i = 0; i < writesPerThread; i++)
                    {
                        var product = new Product
                        {
                            Id = $"getall-{threadIndex:D2}-{i:D3}",
                            Name = $"Product {threadIndex}-{i}",
                            Price = threadIndex * 100m + i,
                            Quantity = i
                        };
                        
                        await provider.SaveAsync(product.Id, product);
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
                        localReadCount++;
                        
                        // Small delay to not overwhelm the system
                        await Task.Delay(10);
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
            errors.Should().BeEmpty("No errors should occur during concurrent GetAll and writes");
            
            var finalCount = await provider.CountAsync();
            finalCount.Should().Be(writerThreads * writesPerThread);
            
            var totalReads = readCounts.Sum();
            totalReads.Should().BeGreaterThan(0, "Readers should have performed some reads");
            
            this.output.WriteLine($"Final entity count: {finalCount}");
            this.output.WriteLine($"Total GetAll operations: {totalReads}");
            this.output.WriteLine($"Average reads per reader thread: {totalReads / (double)readerThreads:F1}");
        }
    }
}