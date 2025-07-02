//-------------------------------------------------------------------------------
// <copyright file="FileSystemProviderPerformanceTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.FileSystem.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
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

    public class FileSystemProviderPerformanceTests : IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly IServiceProvider serviceProvider;
        private readonly string tempDirectory;
        private readonly string providerName = "FileSystemPerfTest";

        public FileSystemProviderPerformanceTests(ITestOutputHelper output)
        {
            this.output = output;

            // Create temp directory for tests
            this.tempDirectory = Path.Combine(Path.GetTempPath(), $"FSPerfTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(this.tempDirectory);

            // Setup DI container
            var services = new ServiceCollection();
            services.AddLogging(builder => builder
                .SetMinimumLevel(LogLevel.Warning)); // Less logging for perf tests

            // Configure FileSystem provider
            var config = new Dictionary<string, string>
            {
                [$"Providers:{this.providerName}:Name"] = this.providerName,
                [$"Providers:{this.providerName}:FilePath"] = Path.Combine(this.tempDirectory, "entities", "dummy.json"),
                [$"Providers:{this.providerName}:UseSubdirectories"] = "true",
                [$"Providers:{this.providerName}:MaxConcurrentFiles"] = "32",
                [$"Providers:{this.providerName}:MaxRetries"] = "3",
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
        public async Task BulkInsert_Performance_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int entityCount = 10000;
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
            
            // Performance assertion
            sw.Elapsed.TotalSeconds.Should().BeLessThan(30, "Bulk insert should complete within 30 seconds");
        }

        [Fact]
        public async Task Sequential_Read_Performance_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int entityCount = 1000;
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
                retrieved.Should().NotBeNull();
            }
            sw.Stop();

            // Assert
            var throughput = entityCount / sw.Elapsed.TotalSeconds;
            this.output.WriteLine($"Sequential read {entityCount} entities: {sw.ElapsedMilliseconds}ms ({throughput:F0} entities/sec)");
            
            sw.Elapsed.TotalSeconds.Should().BeLessThan(30, "Sequential reads should complete within 30 seconds");
        }

        [Fact]
        public async Task Concurrent_Read_Performance_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int entityCount = 1000;
            const int concurrency = 20;
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
                        retrieved.Should().NotBeNull();
                    }
                }));
            }
            
            await Task.WhenAll(tasks);
            sw.Stop();

            // Assert
            var throughput = entityCount / sw.Elapsed.TotalSeconds;
            this.output.WriteLine($"Concurrent read {entityCount} entities with {concurrency} threads: {sw.ElapsedMilliseconds}ms ({throughput:F0} entities/sec)");
            
            sw.Elapsed.TotalSeconds.Should().BeLessThan(2, "Concurrent reads should complete within 2 seconds");
        }

        [Fact]
        public async Task Mixed_Operations_Performance_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int operationCount = 5000;
            var random = new Random(42);
            var products = GenerateProducts(1000);
            
            // Pre-populate with some data
            foreach (var product in products.Take(500))
            {
                await provider.SaveAsync(product.Id, product);
            }

            // Act - Mixed operations
            var sw = Stopwatch.StartNew();
            var operations = 0;
            
            for (int i = 0; i < operationCount; i++)
            {
                var operation = random.Next(4);
                var index = random.Next(products.Count);
                var product = products[index];
                
                switch (operation)
                {
                    case 0: // Create
                        await provider.SaveAsync(product.Id, product);
                        break;
                    case 1: // Read
                        await provider.GetAsync(product.Id);
                        break;
                    case 2: // Update
                        product.Price = random.Next(1, 1000);
                        await provider.SaveAsync(product.Id, product);
                        break;
                    case 3: // Delete
                        await provider.DeleteAsync(product.Id);
                        break;
                }
                operations++;
            }
            sw.Stop();

            // Assert
            var throughput = operations / sw.Elapsed.TotalSeconds;
            this.output.WriteLine($"Mixed operations {operations}: {sw.ElapsedMilliseconds}ms ({throughput:F0} ops/sec)");
            
            sw.Elapsed.TotalSeconds.Should().BeLessThan(10, "Mixed operations should complete within 10 seconds");
        }

        [Fact]
        public async Task GetAll_With_Predicate_Performance_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int entityCount = 5000;
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
            
            sw.Stop();

            // Assert
            expensiveProducts.Count().Should().BeGreaterThan(0);
            cheapProducts.Count().Should().BeGreaterThan(0);
            inStockProducts.Count().Should().BeGreaterThan(0);
            taggedProducts.Count().Should().BeGreaterThan(0);
            
            this.output.WriteLine($"GetAll with predicates on {entityCount} entities: {sw.ElapsedMilliseconds}ms");
            this.output.WriteLine($"  Expensive products: {expensiveProducts.Count()}");
            this.output.WriteLine($"  Cheap products: {cheapProducts.Count()}");
            this.output.WriteLine($"  In stock products: {inStockProducts.Count()}");
            this.output.WriteLine($"  Tagged products: {taggedProducts.Count()}");
            
            sw.Elapsed.TotalSeconds.Should().BeLessThan(60, "GetAll queries should complete within 60 seconds");
        }

        [Fact]
        public async Task Large_Entity_Performance_Test()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int entityCount = 100;
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
                retrieved.Should().NotBeNull();
            }
            readSw.Stop();

            // Assert
            var saveThroughput = entityCount / saveSw.Elapsed.TotalSeconds;
            var readThroughput = entityCount / readSw.Elapsed.TotalSeconds;
            
            this.output.WriteLine($"Large entity save {entityCount}: {saveSw.ElapsedMilliseconds}ms ({saveThroughput:F0} entities/sec)");
            this.output.WriteLine($"Large entity read {entityCount}: {readSw.ElapsedMilliseconds}ms ({readThroughput:F0} entities/sec)");
            
            saveSw.Elapsed.TotalSeconds.Should().BeLessThan(5, "Large entity saves should complete within 5 seconds");
            readSw.Elapsed.TotalSeconds.Should().BeLessThan(2, "Large entity reads should complete within 2 seconds");
        }

        private static List<Product> GenerateProducts(int count)
        {
            var random = new Random(42);
            return Enumerable.Range(1, count).Select(i => new Product
            {
                Id = $"product-{i:D6}",
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