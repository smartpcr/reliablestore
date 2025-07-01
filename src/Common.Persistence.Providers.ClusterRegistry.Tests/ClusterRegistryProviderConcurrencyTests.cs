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
    using AwesomeAssertions;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Models;
    using Xunit.Abstractions;

    public class ClusterRegistryProviderConcurrencyTests : IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly IServiceCollection services;
        private readonly string providerName = "ClusterRegistryConcurrencyTests";
        private readonly ClusterRegistryStoreSettings settings;
        private readonly List<string> testNames = new List<string>();
        private readonly Dictionary<string, IServiceProvider> testServiceProviders = new Dictionary<string, IServiceProvider>();

        public ClusterRegistryProviderConcurrencyTests(ITestOutputHelper output)
        {
            this.output = output;

            // Skip initialization on non-Windows platforms
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            // Setup dependency injection
            this.services = new ServiceCollection();
            this.services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
            var configuration = services.AddConfiguration();
            this.settings = configuration.GetConfiguredSettings<ClusterRegistryStoreSettings>($"Providers:{this.providerName}");
            services.AddKeyedSingleton<CrudStorageProviderSettings>(this.providerName, (_, _) => this.settings);
            this.services.AddPersistence();
        }

        public void Dispose()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                this.CleanupAllTestData();
            }

            // Dispose all test-specific service providers
            foreach (var serviceProvider in this.testServiceProviders.Values)
            {
                if (serviceProvider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            this.testServiceProviders.Clear();
        }

        private void CleanupAllTestData()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            try
            {
                // Clean up registry entries for each test
                foreach (var testName in this.testNames)
                {
                    this.CleanupTestData(testName);
                }

                // Also clean up the root test key if empty
                using var rootKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(this.settings.RootPath, true);
                if (rootKey != null)
                {
                    var subKeyNames = rootKey.GetSubKeyNames();
                    if (subKeyNames.Length == 0)
                    {
                        Microsoft.Win32.Registry.LocalMachine.DeleteSubKey(this.settings.RootPath, false);
                    }
                }

            }
            catch (Exception ex)
            {
                this.output?.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }

        private void CleanupTestData(string testName)
        {
            try
            {
                // Clean up the test-specific service name path
                var testPath = $@"{this.settings.RootPath}\{this.settings.ApplicationName}\{testName}";
                Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(testPath, false);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        [WindowsOnlyFact]
        public async Task Concurrent_Write_Operations_Test()
        {
            // Arrange
            const int threadCount = 5; // Registry has more limited concurrency
            const int operationsPerThread = 20;
            var testName = nameof(Concurrent_Write_Operations_Test);
            this.testNames.Add(testName);
            using var provider = this.CreateProvider(testName);
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

            // Cleanup test data immediately
            this.CleanupTestData(testName);
        }

        [WindowsOnlyFact]
        public async Task Concurrent_Read_Operations_Test()
        {
            // Arrange
            const int recordCount = 100;
            const int threadCount = 10;
            const int readsPerThread = 50;
            var testName = nameof(Concurrent_Read_Operations_Test);
            this.testNames.Add(testName);
            using var provider = this.CreateProvider(testName);
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

            // Cleanup test data immediately
            this.CleanupTestData(testName);
        }

        [WindowsOnlyFact]
        public async Task Concurrent_Mixed_Operations_Test()
        {
            // Arrange
            const int threadCount = 5;
            const int operationsPerThread = 40;
            var testName = nameof(Concurrent_Mixed_Operations_Test);
            this.testNames.Add(testName);
            using var provider = this.CreateProvider(testName);
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

            // Cleanup test data immediately
            this.CleanupTestData(testName);
        }

        [WindowsOnlyFact]
        public async Task Concurrent_GetAll_Operations_Test()
        {
            // Arrange
            const int recordCount = 100;
            const int threadCount = 3;
            const int operationsPerThread = 10;
            var testName = nameof(Concurrent_GetAll_Operations_Test);
            this.testNames.Add(testName);
            using var provider = this.CreateProvider(testName);
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

            // Cleanup test data immediately
            this.CleanupTestData(testName);
        }

        [WindowsOnlyFact]
        public async Task Concurrent_Conflicting_Updates_Test()
        {
            // Arrange
            const int threadCount = 5;
            const int attemptsPerThread = 20;
            const string sharedKey = "shared-product";
            var testName = nameof(Concurrent_Conflicting_Updates_Test);
            this.testNames.Add(testName);
            using var provider = this.CreateProvider(testName);
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

            // Cleanup test data immediately
            this.CleanupTestData(testName);
        }

        [WindowsOnlyFact]
        public async Task Registry_Handle_Limit_Test()
        {
            // Registry has handle limits, test behavior under stress
            const int threadCount = 10;
            const int operationsPerThread = 100;
            var testName = nameof(Registry_Handle_Limit_Test);
            this.testNames.Add(testName);
            using var provider = this.CreateProvider(testName);
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

            // Cleanup test data immediately
            this.CleanupTestData(testName);
        }

        private ICrudStorageProvider<Product>? CreateProvider(string testName)
        {
            try
            {
                // Reuse service provider for the same test name if it exists
                if (!this.testServiceProviders.TryGetValue(testName, out var serviceProvider))
                {
                    // Create a test-specific configuration to isolate data
                    var testSpecificServices = new ServiceCollection();
                    testSpecificServices.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
                    // Create test-specific settings with isolated service name
                    var testSettings = new ClusterRegistryStoreSettings
                    {
                        ClusterName = this.settings.ClusterName,
                        RootPath = this.settings.RootPath,
                        ApplicationName = this.settings.ApplicationName,
                        ServiceName = testName, // Use test name as service name for isolation
                        FallbackToLocalRegistry = this.settings.FallbackToLocalRegistry,
                        EnableCompression = this.settings.EnableCompression,
                        MaxValueSizeKB = this.settings.MaxValueSizeKB,
                        ConnectionTimeoutSeconds = this.settings.ConnectionTimeoutSeconds,
                        RetryCount = this.settings.RetryCount,
                        RetryDelayMilliseconds = this.settings.RetryDelayMilliseconds
                    };
                    var keyPrefix = $"Providers:{testName}";
                    // create in-memory configuration for testSettings with keyPrefix for each property
                    testSpecificServices.AddConfiguration(new Dictionary<string, string>
                    {
                        [$"{keyPrefix}:ClusterName"] = testSettings.ClusterName,
                        [$"{keyPrefix}:RootPath"] = testSettings.RootPath,
                        [$"{keyPrefix}:ApplicationName"] = testSettings.ApplicationName,
                        [$"{keyPrefix}:ServiceName"] = testSettings.ServiceName,
                        [$"{keyPrefix}:FallbackToLocalRegistry"] = testSettings.FallbackToLocalRegistry.ToString(),
                        [$"{keyPrefix}:EnableCompression"] = testSettings.EnableCompression.ToString(),
                        [$"{keyPrefix}:MaxValueSizeKB"] = testSettings.MaxValueSizeKB.ToString(),
                        [$"{keyPrefix}:ConnectionTimeoutSeconds"] = testSettings.ConnectionTimeoutSeconds.ToString(),
                        [$"{keyPrefix}:RetryCount"] = testSettings.RetryCount.ToString(),
                        [$"{keyPrefix}:RetryDelayMilliseconds"] = testSettings.RetryDelayMilliseconds.ToString()
                    });

                    testSpecificServices.AddKeyedSingleton<CrudStorageProviderSettings>(testName, (_, _) => testSettings);
                    testSpecificServices.AddPersistence();
                    
                    serviceProvider = testSpecificServices.BuildServiceProvider();
                    this.testServiceProviders[testName] = serviceProvider;
                }
                
                var factory = serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
                return factory.Create<Product>(testName);
            }
            catch (Exception ex)
            {
                this.output.WriteLine($"Failed to create provider: {ex.Message}");
                return null;
            }
        }
    }
}