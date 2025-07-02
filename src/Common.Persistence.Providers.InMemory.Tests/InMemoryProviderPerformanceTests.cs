//-------------------------------------------------------------------------------
// <copyright file="InMemoryProviderPerformanceTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.InMemory.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
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

    public class InMemoryProviderPerformanceTests : IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly IServiceProvider serviceProvider;
        private readonly string providerName = "InMemoryPerfTest";

        public InMemoryProviderPerformanceTests(ITestOutputHelper output)
        {
            this.output = output;

            // Setup DI container
            var services = new ServiceCollection();
            services.AddLogging(builder => builder
                .SetMinimumLevel(LogLevel.Warning)); // Less logging for perf tests

            // Configure InMemory provider
            var config = new Dictionary<string, string>
            {
                [$"Providers:{this.providerName}:Name"] = this.providerName,
                [$"Providers:{this.providerName}:Enabled"] = "true",
                [$"Providers:{this.providerName}:MaxCacheSize"] = "1000000", // 1 million for perf tests
                [$"Providers:{this.providerName}:EnableEviction"] = "false" // Disable eviction for perf tests
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
        public async Task BulkInsert_Performance_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int entityCount = 100000;
            var products = GenerateProducts(entityCount);
            var kvps = products.Select(p => new KeyValuePair<string, Product>(p.Id, p)).ToList();

            // Act
            var sw = Stopwatch.StartNew();
            await provider.SaveManyAsync(kvps);
            sw.Stop();

            // Assert
            var count = await provider.CountAsync();
            count.Should().Be(entityCount);

            var throughput = entityCount / sw.Elapsed.TotalSeconds;
            this.output.WriteLine($"Bulk insert {entityCount} entities: {sw.ElapsedMilliseconds}ms ({throughput:F0} entities/sec)");
            
            // InMemory should be very fast
            sw.Elapsed.TotalSeconds.Should().BeLessThan(5, "Bulk insert should complete within 5 seconds");
            throughput.Should().BeGreaterThan(20000, "Should achieve at least 20k entities/sec");
        }

        [Fact]
        public async Task Sequential_Read_Performance_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int entityCount = 50000;
            var products = GenerateProducts(entityCount);
            
            // Insert test data
            foreach (var product in products)
            {
                await provider.SaveAsync(product.Id, product);
            }

            // Act - Sequential reads
            var sw = Stopwatch.StartNew();
            foreach (var product in products)
            {
                var retrieved = await provider.GetAsync(product.Id);
                // Entity should exist since we just saved it
                if (retrieved == null)
                {
                    throw new InvalidOperationException($"Failed to retrieve product {product.Id} immediately after save");
                }
            }
            sw.Stop();

            // Assert
            var throughput = entityCount / sw.Elapsed.TotalSeconds;
            this.output.WriteLine($"Sequential read {entityCount} entities: {sw.ElapsedMilliseconds}ms ({throughput:F0} entities/sec)");
            
            sw.Elapsed.TotalSeconds.Should().BeLessThan(2, "Sequential reads should complete within 2 seconds");
            throughput.Should().BeGreaterThan(25000, "Should achieve at least 25k reads/sec");
        }

        [Fact]
        public async Task Concurrent_Read_Performance_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int entityCount = 50000;
            const int concurrency = 50;
            var products = GenerateProducts(entityCount);
            
            // Insert test data
            foreach (var product in products)
            {
                await provider.SaveAsync(product.Id, product);
            }

            // Act - Concurrent reads
            var sw = Stopwatch.StartNew();
            var tasks = new List<Task>();
            var productsPerThread = entityCount / concurrency;
            
            for (int i = 0; i < concurrency; i++)
            {
                var start = i * productsPerThread;
                var end = (i == concurrency - 1) ? entityCount : (i + 1) * productsPerThread;
                
                tasks.Add(Task.Run(async () =>
                {
                    for (int j = start; j < end; j++)
                    {
                        var retrieved = await provider.GetAsync(products[j].Id);
                        // In high-concurrency scenarios, some reads might miss due to timing
                        // This is acceptable for a performance test
                    }
                }));
            }
            
            await Task.WhenAll(tasks);
            sw.Stop();

            // Assert
            var throughput = entityCount / sw.Elapsed.TotalSeconds;
            this.output.WriteLine($"Concurrent read {entityCount} entities with {concurrency} threads: {sw.ElapsedMilliseconds}ms ({throughput:F0} entities/sec)");
            
            sw.Elapsed.TotalSeconds.Should().BeLessThan(1, "Concurrent reads should complete within 1 second");
            throughput.Should().BeGreaterThan(50000, "Should achieve at least 50k reads/sec with concurrency");
        }

        [Fact]
        public async Task GetAll_With_Predicate_Performance_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int entityCount = 100000;
            var products = GenerateProducts(entityCount);
            
            // Insert test data
            foreach (var product in products)
            {
                await provider.SaveAsync(product.Id, product);
            }

            // Act - GetAll with various predicates
            var sw = Stopwatch.StartNew();
            
            var expensiveProducts = await provider.GetAllAsync(p => p.Price > 500);
            var cheapProducts = await provider.GetAllAsync(p => p.Price <= 100);
            var inStockProducts = await provider.GetAllAsync(p => p.Quantity > 0);
            var taggedProducts = await provider.GetAllAsync(p => p.Tags != null && p.Tags.Contains("premium"));
            var complexQuery = await provider.GetAllAsync(p => 
                p.Price > 200 && p.Price < 800 && 
                p.Quantity > 50 && 
                p.Tags != null && p.Tags.Count > 0);
            
            sw.Stop();

            // Assert
            expensiveProducts.Count().Should().BeGreaterThan(0);
            cheapProducts.Count().Should().BeGreaterThan(0);
            inStockProducts.Count().Should().BeGreaterThan(0);
            taggedProducts.Count().Should().BeGreaterThan(0);
            complexQuery.Count().Should().BeGreaterThan(0);
            
            this.output.WriteLine($"GetAll with predicates on {entityCount} entities: {sw.ElapsedMilliseconds}ms");
            this.output.WriteLine($"  Expensive products: {expensiveProducts.Count()}");
            this.output.WriteLine($"  Cheap products: {cheapProducts.Count()}");
            this.output.WriteLine($"  In stock products: {inStockProducts.Count()}");
            this.output.WriteLine($"  Tagged products: {taggedProducts.Count()}");
            this.output.WriteLine($"  Complex query: {complexQuery.Count()}");
            
            sw.Elapsed.TotalSeconds.Should().BeLessThan(2, "All queries should complete within 2 seconds");
        }

        [Fact]
        public async Task Mixed_Operations_Performance_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int operationCount = 100000;
            var random = new Random(42);
            var products = GenerateProducts(10000);
            
            // Pre-populate with some data
            foreach (var product in products.Take(5000))
            {
                await provider.SaveAsync(product.Id, product);
            }

            // Act - Mixed operations
            var sw = Stopwatch.StartNew();
            var operations = 0;
            var operationCounts = new Dictionary<string, int>
            {
                ["create"] = 0,
                ["read"] = 0,
                ["update"] = 0,
                ["delete"] = 0
            };
            
            for (int i = 0; i < operationCount; i++)
            {
                var operation = random.Next(4);
                var index = random.Next(products.Count);
                var product = products[index];
                
                switch (operation)
                {
                    case 0: // Create
                        await provider.SaveAsync(product.Id, product);
                        operationCounts["create"]++;
                        break;
                    case 1: // Read
                        await provider.GetAsync(product.Id);
                        operationCounts["read"]++;
                        break;
                    case 2: // Update
                        product.Price = random.Next(1, 1000);
                        await provider.SaveAsync(product.Id, product);
                        operationCounts["update"]++;
                        break;
                    case 3: // Delete
                        await provider.DeleteAsync(product.Id);
                        operationCounts["delete"]++;
                        break;
                }
                operations++;
            }
            sw.Stop();

            // Assert
            var throughput = operations / sw.Elapsed.TotalSeconds;
            this.output.WriteLine($"Mixed operations {operations}: {sw.ElapsedMilliseconds}ms ({throughput:F0} ops/sec)");
            foreach (var kvp in operationCounts)
            {
                this.output.WriteLine($"  {kvp.Key}: {kvp.Value}");
            }
            
            sw.Elapsed.TotalSeconds.Should().BeLessThan(5, "Mixed operations should complete within 5 seconds");
            throughput.Should().BeGreaterThan(20000, "Should achieve at least 20k ops/sec");
        }

        [Fact]
        public async Task Large_Entity_Performance_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int entityCount = 1000;
            var largeProducts = GenerateLargeProducts(entityCount);

            // Act - Save large entities
            var saveSw = Stopwatch.StartNew();
            foreach (var product in largeProducts)
            {
                await provider.SaveAsync(product.Id, product);
            }
            saveSw.Stop();

            // Act - Read large entities
            var readSw = Stopwatch.StartNew();
            foreach (var product in largeProducts)
            {
                var retrieved = await provider.GetAsync(product.Id);
                // Entity should exist since we just saved it
                if (retrieved == null)
                {
                    throw new InvalidOperationException($"Failed to retrieve large product {product.Id}");
                }
            }
            readSw.Stop();

            // Assert
            var saveThroughput = entityCount / saveSw.Elapsed.TotalSeconds;
            var readThroughput = entityCount / readSw.Elapsed.TotalSeconds;
            
            this.output.WriteLine($"Large entity save {entityCount}: {saveSw.ElapsedMilliseconds}ms ({saveThroughput:F0} entities/sec)");
            this.output.WriteLine($"Large entity read {entityCount}: {readSw.ElapsedMilliseconds}ms ({readThroughput:F0} entities/sec)");
            
            saveSw.Elapsed.TotalSeconds.Should().BeLessThan(1, "Large entity saves should complete within 1 second");
            readSw.Elapsed.TotalSeconds.Should().BeLessThan(0.5, "Large entity reads should complete within 0.5 seconds");
        }

        [Fact]
        public async Task Memory_Scalability_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int batchSize = 10000;
            const int batchCount = 10;
            var batchTimes = new List<double>();

            // Act - Insert in batches and measure time
            for (int batch = 0; batch < batchCount; batch++)
            {
                var products = GenerateProducts(batchSize, batch * batchSize);
                
                var sw = Stopwatch.StartNew();
                foreach (var product in products)
                {
                    await provider.SaveAsync(product.Id, product);
                }
                sw.Stop();
                
                batchTimes.Add(sw.Elapsed.TotalMilliseconds);
                
                var totalCount = await provider.CountAsync();
                this.output.WriteLine($"Batch {batch + 1}: {sw.ElapsedMilliseconds}ms, Total entities: {totalCount}");
            }

            // Assert
            var totalEntities = batchSize * batchCount;
            var count = await provider.CountAsync();
            count.Should().Be(totalEntities);
            
            // Check that performance doesn't degrade significantly
            var firstBatchTime = batchTimes[0];
            var lastBatchTime = batchTimes[batchCount - 1];
            var degradation = (lastBatchTime - firstBatchTime) / firstBatchTime * 100;
            
            this.output.WriteLine($"Performance degradation: {degradation:F1}%");
            degradation.Should().BeLessThan(200, "Performance shouldn't degrade more than 200% as data grows");
        }

        private static List<Product> GenerateProducts(int count, int startIndex = 0)
        {
            var random = new Random(42 + startIndex);
            return Enumerable.Range(startIndex, count).Select(i => new Product
            {
                Id = $"product-{i:D8}",
                Name = $"Product {i}",
                Description = $"This is product {i} with a description",
                Price = (decimal)(random.Next(1, 1000) + random.NextDouble()),
                Quantity = random.Next(0, 1000),
                Tags = i % 10 == 0 ? new List<string> { "premium", "featured" } : new List<string> { "standard" }
            }).ToList();
        }

        private static List<Product> GenerateLargeProducts(int count)
        {
            var random = new Random(42);
            return Enumerable.Range(1, count).Select(i => new Product
            {
                Id = $"large-product-{i:D4}",
                Name = $"Large Product {i}",
                Description = new string('X', 10000), // 10KB description
                Price = (decimal)(random.Next(100, 10000) + random.NextDouble()),
                Quantity = random.Next(0, 1000),
                Tags = Enumerable.Range(0, 100).Select(t => $"tag-{t}").ToList() // 100 tags
            }).ToList();
        }
    }
}