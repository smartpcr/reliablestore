//-------------------------------------------------------------------------------
// <copyright file="SqlServerProviderConcurrencyTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.SqlServer.Tests
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using AwesomeAssertions;
    using Common.Persistence.Configuration;
    using Common.Persistence.Factory;
    using DotNetEnv;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Models;
    using Xunit;
    using Xunit.Abstractions;

    public class SqlServerProviderConcurrencyTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper output;
        private readonly string providerName = "SqlServerConcurrencyTest";
        private IServiceProvider serviceProvider;

        public SqlServerProviderConcurrencyTests(ITestOutputHelper output)
        {
            this.output = output;
            Env.Load();
        }

        public async Task InitializeAsync()
        {
            // Setup DI container
            var services = new ServiceCollection();
            services.AddLogging(builder => builder
                .SetMinimumLevel(LogLevel.Debug)
                .AddXunit(this.output));

            // Configure SQL Server provider
            var config = new Dictionary<string, string>
            {
                [$"Providers:{this.providerName}:Name"] = this.providerName,
                [$"Providers:{this.providerName}:AssemblyName"] = "CRP.Common.Persistence.Providers.SqlServer",
                [$"Providers:{this.providerName}:TypeName"] = "Common.Persistence.Providers.SqlServer.SqlServerProvider`1",
                [$"Providers:{this.providerName}:Enabled"] = "true",
                [$"Providers:{this.providerName}:Capabilities"] = "1",
                [$"Providers:{this.providerName}:Host"] = "localhost",
                [$"Providers:{this.providerName}:Port"] = "1433",
                [$"Providers:{this.providerName}:DbName"] = "ReliableStoreConcTest",
                [$"Providers:{this.providerName}:UserId"] = "sa",
                [$"Providers:{this.providerName}:Password"] = Environment.GetEnvironmentVariable("DB_PASSWORD")!,
                [$"Providers:{this.providerName}:CommandTimeout"] = "60",
                [$"Providers:{this.providerName}:EnableRetryLogic"] = "true",
                [$"Providers:{this.providerName}:MaxRetryCount"] = "3",
                [$"Providers:{this.providerName}:CreateTableIfNotExists"] = "true",
                [$"Providers:{this.providerName}:CreateDatabaseIfNotExists"] = "true"
            };

            var configuration = services.AddConfiguration(config);

            // Register settings
            var settings = configuration.GetConfiguredSettings<SqlServerProviderSettings>($"Providers:{this.providerName}");
            services.AddKeyedSingleton<CrudStorageProviderSettings>(this.providerName, (_, _) => settings);
            services.AddSingleton<IConfigReader, JsonConfigReader>();
            services.AddPersistence();

            this.serviceProvider = services.BuildServiceProvider();
        }

        public async Task DisposeAsync()
        {
            // Cleanup handled by GlobalAssemblyInit
            await Task.CompletedTask;
        }

        [Fact]
        public async Task ConcurrentWrites_DifferentKeys_AllSucceed()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            const int threadCount = 10;
            const int itemsPerThread = 50;
            var allTasks = new List<Task>();
            var errors = new ConcurrentBag<Exception>();

            // Act
            for (int t = 0; t < threadCount; t++)
            {
                var threadId = t;
                var task = Task.Run(async () =>
                {
                    try
                    {
                        for (int i = 0; i < itemsPerThread; i++)
                        {
                            var product = new Product
                            {
                                Id = $"concurrent-product-t{threadId}-i{i}",
                                Name = $"Concurrent Product Thread {threadId} Item {i}",
                                Quantity = threadId * 100 + i,
                                Price = 99.99m
                            };

                            await provider.SaveAsync(product.Key, product);
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                });
                allTasks.Add(task);
            }

            await Task.WhenAll(allTasks);

            // Assert
            errors.Should().BeEmpty();

            // Verify all items were saved
            var totalCount = await provider.CountAsync();
            totalCount.Should().BeGreaterThanOrEqualTo(threadCount * itemsPerThread);

            // Spot check some items
            for (int t = 0; t < threadCount; t += 3)
            {
                for (int i = 0; i < itemsPerThread; i += 10)
                {
                    var key = $"Product/concurrent-product-t{t}-i{i}";
                    var exists = await provider.ExistsAsync(key);
                    exists.Should().BeTrue($"Product with key {key} should exist");
                }
            }
        }

        [Fact]
        public async Task ConcurrentUpdates_SameKey_LastWriteWins()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            var product = new Product
            {
                Id = "concurrent-update-product",
                Name = "Original Name",
                Quantity = 0,
                Version = 1
            };

            await provider.SaveAsync(product.Key, product);

            const int updateCount = 20;
            var updateTasks = new List<Task>();
            var updateVersions = new ConcurrentBag<long>();

            // Act
            var barrier = new Barrier(updateCount);

            for (int i = 0; i < updateCount; i++)
            {
                var updateId = i;
                var task = Task.Run(async () =>
                {
                    // Wait for all threads to be ready
                    barrier.SignalAndWait();

                    // Each thread updates with its own values
                    var updatedProduct = new Product
                    {
                        Id = "concurrent-update-product",
                        Name = $"Updated by Thread {updateId}",
                        Quantity = updateId * 10,
                        Version = updateId + 2
                    };

                    await provider.SaveAsync(updatedProduct.Key, updatedProduct);
                    updateVersions.Add(updatedProduct.Version);
                });
                updateTasks.Add(task);
            }

            await Task.WhenAll(updateTasks);

            // Assert
            var finalProduct = await provider.GetAsync(product.Key);
            finalProduct.Should().NotBeNull();

            // The final state should be from one of the updates
            finalProduct.Name.Should().StartWith("Updated by Thread");
            finalProduct.Version.Should().BeGreaterThanOrEqualTo(2);
            updateVersions.Should().Contain(finalProduct.Version);

            this.output.WriteLine($"Final product state: Name='{finalProduct.Name}', Version={finalProduct.Version}, Quantity={finalProduct.Quantity}");
        }

        [Fact]
        public async Task ConcurrentReadsAndWrites_MixedOperations_DataConsistency()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            const int operationCount = 100;
            var random = new Random(42);
            var productIds = Enumerable.Range(1, 10).Select(i => $"mixed-concurrent-{i}").ToList();

            // Initialize products
            foreach (var id in productIds)
            {
                var product = new Product
                {
                    Id = id,
                    Name = $"Initial Product {id}",
                    Quantity = 100,
                    Version = 1
                };
                await provider.SaveAsync(product.Key, product);
            }

            var operations = new ConcurrentBag<string>();
            var errors = new ConcurrentBag<Exception>();

            // Act
            var tasks = Enumerable.Range(0, operationCount).Select(_ => Task.Run(async () =>
            {
                try
                {
                    var productId = productIds[random.Next(productIds.Count)];
                    var operation = random.Next(3);

                    switch (operation)
                    {
                        case 0: // Read
                            var product = await provider.GetAsync($"Product/{productId}");
                            if (product != null)
                            {
                                operations.Add($"Read: {productId}, Quantity={product.Quantity}");
                            }
                            break;

                        case 1: // Update quantity
                            var existing = await provider.GetAsync($"Product/{productId}");
                            if (existing != null)
                            {
                                existing.Quantity += random.Next(-10, 11);
                                existing.Version++;
                                await provider.SaveAsync(existing.Key, existing);
                                operations.Add($"Update: {productId}, NewQuantity={existing.Quantity}");
                            }
                            break;

                        case 2: // Check exists
                            var exists = await provider.ExistsAsync($"Product/{productId}");
                            operations.Add($"Exists: {productId}={exists}");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            })).ToList();

            await Task.WhenAll(tasks);

            // Assert
            errors.Should().BeEmpty();
            operations.Should().HaveCount(operationCount);

            // All products should still exist
            foreach (var id in productIds)
            {
                var exists = await provider.ExistsAsync($"Product/{id}");
                exists.Should().BeTrue($"Product {id} should still exist");
            }

            this.output.WriteLine($"Completed {operations.Count} concurrent operations without errors");
        }

        [Fact]
        public async Task ConcurrentBulkOperations_NoDeadlocks()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            const int threadCount = 5;
            const int bulkSize = 20;
            var allTasks = new List<Task>();
            var errors = new ConcurrentBag<Exception>();

            // Act
            for (int t = 0; t < threadCount; t++)
            {
                var threadId = t;
                var task = Task.Run(async () =>
                {
                    try
                    {
                        // Each thread performs bulk save
                        var products = Enumerable.Range(0, bulkSize).Select(i => new Product
                        {
                            Id = $"bulk-thread{threadId}-item{i}",
                            Name = $"Bulk Product T{threadId} I{i}",
                            Quantity = threadId * 1000 + i
                        }).ToList();

                        var entities = products.Select(p => new KeyValuePair<string, Product>(p.Key, p));
                        await provider.SaveManyAsync(entities);

                        // Then reads them back
                        var keys = products.Select(p => p.Key).ToList();
                        var retrieved = (await provider.GetManyAsync(keys)).ToArray();

                        if (retrieved.Count() != bulkSize)
                        {
                            throw new Exception($"Thread {threadId}: Expected {bulkSize} items, got {retrieved.Count()}");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                        this.output.WriteLine($"Thread {threadId} error: {ex.Message}");
                    }
                });
                allTasks.Add(task);
            }

            await Task.WhenAll(allTasks);

            // Assert
            errors.Should().BeEmpty("No deadlocks or errors should occur");

            var totalCount = await provider.CountAsync();
            totalCount.Should().Be(threadCount * bulkSize);
        }

        [Fact]
        public async Task StressTest_HighConcurrency_RemainsStable()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            const int duration = 5; // seconds
            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(duration));
            var operationCounts = new ConcurrentDictionary<string, int>();
            var errors = new ConcurrentBag<Exception>();

            // Act
            var tasks = new List<Task>();

            // Writer tasks
            for (int i = 0; i < 3; i++)
            {
                var writerId = i;
                tasks.Add(Task.Run(async () =>
                {
                    var count = 0;
                    while (!cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        try
                        {
                            var product = new Product
                            {
                                Id = $"stress-writer{writerId}-{count++}",
                                Name = $"Stress Test Product",
                                Quantity = count
                            };
                            await provider.SaveAsync(product.Key, product);
                            operationCounts.AddOrUpdate("writes", 1, (_, v) => v + 1);
                        }
                        catch (Exception ex) when (!cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            errors.Add(ex);
                        }
                    }
                }));
            }

            // Reader tasks
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    while (!cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        try
                        {
                            await provider.CountAsync(cancellationToken: cancellationTokenSource.Token);
                            operationCounts.AddOrUpdate("reads", 1, (_, v) => v + 1);
                            await Task.Delay(10); // Small delay to prevent overwhelming
                        }
                        catch (Exception ex) when (!cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            errors.Add(ex);
                        }
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            errors.Should().BeEmpty();
            operationCounts.Should().ContainKey("writes");
            operationCounts.Should().ContainKey("reads");

            this.output.WriteLine($"Stress test completed: {operationCounts["writes"]} writes, {operationCounts["reads"]} reads");

            // Should have completed reasonable number of operations
            operationCounts["writes"].Should().BeGreaterThan(50);
            operationCounts["reads"].Should().BeGreaterThan(100);
        }
    }
}