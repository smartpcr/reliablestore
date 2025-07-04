//-------------------------------------------------------------------------------
// <copyright file="SQLiteProviderConcurrencyTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.SQLite.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using AwesomeAssertions;
    using Common.Persistence.Configuration;
    using Common.Persistence.Factory;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Models;
    using Xunit;
    using Xunit.Abstractions;

    public class SQLiteProviderConcurrencyTests : IAsyncLifetime, IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly string providerName = "SQLiteConcTest";
        private readonly string schemaName = "test2";
        private readonly string databasePath;
        private IServiceProvider serviceProvider;
        private ICrudStorageProviderFactory? factory;

        public SQLiteProviderConcurrencyTests(ITestOutputHelper output)
        {
            this.output = output;
            this.databasePath = Path.Combine(Directory.GetCurrentDirectory(), $"test_{Guid.NewGuid():N}.db");
        }

        public Task InitializeAsync()
        {
            // Setup DI container
            var services = new ServiceCollection();
            services.AddLogging(builder => builder
                .SetMinimumLevel(LogLevel.Debug)
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
                [$"Providers:{this.providerName}:CommandTimeout"] = "30",
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
        public async Task ConcurrentReads_NoConflicts()
        {
            // Arrange
            using var provider = this.factory!.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            var product = new Product
            {
                Id = "concurrent-read-test",
                Name = "Product for Concurrent Read",
                Quantity = 100
            };

            await provider.SaveAsync(product.Key, product);

            const int concurrentReads = 10;
            var tasks = new Task<Product?>[concurrentReads];

            // Act
            for (int i = 0; i < concurrentReads; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    using var readProvider = this.factory!.Create<Product>(this.providerName);
                    return await readProvider.GetAsync(product.Key);
                });
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().AllSatisfy(result =>
            {
                result.Should().NotBeNull();
                result!.Id.Should().Be(product.Id);
                result.Name.Should().Be(product.Name);
                result.Quantity.Should().Be(product.Quantity);
            });
        }

        [Fact]
        public async Task ConcurrentWrites_LastWriteWins()
        {
            // Arrange
            const string productId = "concurrent-write-test";
            const int concurrentWrites = 5;
            var writeCompletionOrder = new List<int>();
            var writeCompletionLock = new object();

            var tasks = new Task[concurrentWrites];

            // Act
            for (int i = 0; i < concurrentWrites; i++)
            {
                var writeIndex = i;
                tasks[i] = Task.Run(async () =>
                {
                    using var provider = this.factory!.Create<Product>(this.providerName);
                    var product = new Product
                    {
                        Id = productId,
                        Name = $"Product Version {writeIndex}",
                        Quantity = writeIndex * 10,
                        Version = writeIndex
                    };

                    await provider.SaveAsync(product.Key, product);

                    lock (writeCompletionLock)
                    {
                        writeCompletionOrder.Add(writeIndex);
                    }
                });
            }

            await Task.WhenAll(tasks);

            // Assert - verify last write wins
            using var readProvider = this.factory!.Create<Product>(this.providerName);
            var finalProduct = await readProvider.GetAsync($"Product/{productId}");

            finalProduct.Should().NotBeNull();
            // The product should have data from one of the writes
            var versionNumber = int.Parse(finalProduct!.Name.Split(' ').Last());
            finalProduct.Version.Should().Be(versionNumber);
            finalProduct.Quantity.Should().Be(versionNumber * 10);
        }

        [Fact]
        public async Task MixedOperations_ConcurrentReadWriteDelete()
        {
            // Arrange
            const int operationCount = 20;
            var random = new Random(42);
            var productIds = Enumerable.Range(1, 5).Select(i => $"mixed-op-{i}").ToList();

            // Seed initial data
            using (var provider = this.factory!.Create<Product>(this.providerName))
            {
                foreach (var id in productIds)
                {
                    var product = new Product
                    {
                        Id = id,
                        Name = $"Initial Product {id}",
                        Quantity = 100
                    };
                    await provider.SaveAsync(product.Key, product);
                }
            }

            var tasks = new Task[operationCount];
            var exceptions = new List<Exception>();

            // Act
            for (int i = 0; i < operationCount; i++)
            {
                var operationType = random.Next(3);
                var productId = productIds[random.Next(productIds.Count)];

                tasks[i] = Task.Run(async () =>
                {
                    try
                    {
                        using var provider = this.factory!.Create<Product>(this.providerName);

                        switch (operationType)
                        {
                            case 0: // Read
                                await provider.GetAsync($"Product/{productId}");
                                break;
                            case 1: // Write
                                var product = new Product
                                {
                                    Id = productId,
                                    Name = $"Updated Product {productId} at {DateTime.UtcNow:O}",
                                    Quantity = random.Next(1, 200)
                                };
                                await provider.SaveAsync(product.Key, product);
                                break;
                            case 2: // Delete
                                await provider.DeleteAsync($"Product/{productId}");
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });
            }

            await Task.WhenAll(tasks);

            // Assert - no exceptions should occur
            exceptions.Should().BeEmpty();

            // Verify final state is consistent
            using var finalProvider = this.factory!.Create<Product>(this.providerName);
            var count = await finalProvider.CountAsync();
            count.Should().BeGreaterThanOrEqualTo(0).And.BeLessThanOrEqualTo(productIds.Count);
        }

        [Fact]
        public async Task BulkOperations_ConcurrentSaveMany()
        {
            // Arrange
            const int concurrentBulkOps = 3;
            const int itemsPerBulkOp = 10;
            var tasks = new Task[concurrentBulkOps];

            // Act
            for (int i = 0; i < concurrentBulkOps; i++)
            {
                var bulkIndex = i;
                tasks[i] = Task.Run(async () =>
                {
                    using var provider = this.factory!.Create<Product>(this.providerName);

                    var products = Enumerable.Range(1, itemsPerBulkOp).Select(j => new Product
                    {
                        Id = $"bulk-{bulkIndex}-item-{j}",
                        Name = $"Bulk {bulkIndex} Item {j}",
                        Quantity = bulkIndex * 100 + j
                    }).ToList();

                    var entities = products.Select(p => new KeyValuePair<string, Product>(p.Key, p));
                    await provider.SaveManyAsync(entities);
                });
            }

            await Task.WhenAll(tasks);

            // Assert
            using var readProvider = this.factory!.Create<Product>(this.providerName);
            var allProducts = await readProvider.GetAllAsync();
            var productList = allProducts.ToList();

            // Should have all items from all bulk operations
            productList.Where(p => p.Id.StartsWith("bulk-")).Should().HaveCount(concurrentBulkOps * itemsPerBulkOp);
        }

        [Fact]
        public async Task StressTest_HighConcurrencyMixedOperations()
        {
            // Arrange
            const int duration = 3; // seconds
            const int threadCount = 10;
            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(duration));
            var operationCounts = new int[4]; // Read, Write, Delete, Clear
            var errors = 0;

            var tasks = new Task[threadCount];

            // Act
            for (int i = 0; i < threadCount; i++)
            {
                var threadIndex = i;
                tasks[i] = Task.Run(async () =>
                {
                    var random = new Random(threadIndex);
                    while (!cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        try
                        {
                            using var provider = this.factory!.Create<Product>(this.providerName);
                            var operation = random.Next(4);

                            switch (operation)
                            {
                                case 0: // Read
                                    var key = $"Product/stress-{random.Next(100)}";
                                    await provider.GetAsync(key);
                                    Interlocked.Increment(ref operationCounts[0]);
                                    break;
                                case 1: // Write
                                    var productId = $"stress-{random.Next(100)}";
                                    var product = new Product
                                    {
                                        Id = productId,
                                        Name = $"Stress Test Product {productId}",
                                        Quantity = random.Next(1000)
                                    };
                                    await provider.SaveAsync(product.Key, product);
                                    Interlocked.Increment(ref operationCounts[1]);
                                    break;
                                case 2: // Delete
                                    var deleteKey = $"Product/stress-{random.Next(100)}";
                                    await provider.DeleteAsync(deleteKey);
                                    Interlocked.Increment(ref operationCounts[2]);
                                    break;
                                case 3: // Clear (less frequent)
                                    if (random.Next(100) < 5) // 5% chance
                                    {
                                        await provider.ClearAsync();
                                        Interlocked.Increment(ref operationCounts[3]);
                                    }
                                    break;
                            }

                            await Task.Delay(random.Next(10, 50), cancellationTokenSource.Token);
                        }
                        catch (OperationCanceledException) when (cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            // Expected cancellation, don't count as error
                        }
                        catch
                        {
                            Interlocked.Increment(ref errors);
                        }
                    }
                });
            }

            await Task.WhenAll(tasks);

            // Assert
            this.output.WriteLine($"Operations completed - Reads: {operationCounts[0]}, Writes: {operationCounts[1]}, Deletes: {operationCounts[2]}, Clears: {operationCounts[3]}");
            this.output.WriteLine($"Errors: {errors}");

            operationCounts.Sum().Should().BeGreaterThan(0, "Should have completed some operations");
            errors.Should().Be(0, "Should not have any errors during stress test");
        }
    }
}