//-------------------------------------------------------------------------------
// <copyright file="EsentProviderPerformanceTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.Esent.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.InteropServices;
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

    public class EsentProviderPerformanceTests : IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly IServiceCollection services;
        private readonly string providerName = "EsentProviderPerfTests";
        private readonly List<string> tempDatabases = new List<string>();

        public EsentProviderPerformanceTests(ITestOutputHelper output)
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
        public async Task BulkInsert_Performance_Test()
        {
            // Arrange
            const int recordCount = 10000;
            using var provider = this.CreateProvider(nameof(BulkInsert_Performance_Test));
            var products = GenerateProducts(recordCount);

            // Act
            var stopwatch = Stopwatch.StartNew();
            foreach (var product in products)
            {
                await provider.SaveAsync(product.Id, product);
            }
            stopwatch.Stop();

            // Assert
            var throughput = recordCount / stopwatch.Elapsed.TotalSeconds;
            this.output.WriteLine($"Bulk insert {recordCount} records: {stopwatch.ElapsedMilliseconds}ms");
            this.output.WriteLine($"Throughput: {throughput:F2} records/second");

            // Verify data
            var count = await provider.CountAsync();
            count.Should().Be(recordCount);
        }

        [WindowsOnlyFact]
        public async Task BulkRead_Performance_Test()
        {
            // Arrange
            const int recordCount = 5000;
            using var provider = this.CreateProvider(nameof(BulkRead_Performance_Test));
            var products = GenerateProducts(recordCount);
            
            // Insert test data
            foreach (var product in products)
            {
                await provider.SaveAsync(product.Id, product);
            }

            // Act - Sequential reads
            var stopwatch = Stopwatch.StartNew();
            foreach (var product in products)
            {
                var result = await provider.GetAsync(product.Id);
                result.Should().NotBeNull();
            }
            stopwatch.Stop();

            // Assert
            var throughput = recordCount / stopwatch.Elapsed.TotalSeconds;
            this.output.WriteLine($"Sequential read {recordCount} records: {stopwatch.ElapsedMilliseconds}ms");
            this.output.WriteLine($"Throughput: {throughput:F2} reads/second");
        }

        [WindowsOnlyFact]
        public async Task RandomAccess_Performance_Test()
        {
            // Arrange
            const int recordCount = 5000;
            const int accessCount = 10000;
            using var provider = this.CreateProvider(nameof(RandomAccess_Performance_Test));
            var products = GenerateProducts(recordCount);
            
            // Insert test data
            foreach (var product in products)
            {
                await provider.SaveAsync(product.Id, product);
            }

            var random = new Random(42);
            var randomIds = Enumerable.Range(0, accessCount)
                .Select(_ => products[random.Next(recordCount)].Id)
                .ToList();

            // Act - Random access
            var stopwatch = Stopwatch.StartNew();
            foreach (var id in randomIds)
            {
                var result = await provider.GetAsync(id);
                result.Should().NotBeNull();
            }
            stopwatch.Stop();

            // Assert
            var throughput = accessCount / stopwatch.Elapsed.TotalSeconds;
            this.output.WriteLine($"Random access {accessCount} reads from {recordCount} records: {stopwatch.ElapsedMilliseconds}ms");
            this.output.WriteLine($"Throughput: {throughput:F2} reads/second");
        }

        [WindowsOnlyFact]
        public async Task Update_Performance_Test()
        {
            // Arrange
            const int recordCount = 5000;
            using var provider = this.CreateProvider(nameof(Update_Performance_Test));
            var products = GenerateProducts(recordCount);
            
            // Insert test data
            foreach (var product in products)
            {
                await provider.SaveAsync(product.Id, product);
            }

            // Modify products
            foreach (var product in products)
            {
                product.Quantity += 10;
                product.Price *= 1.1m;
            }

            // Act - Updates
            var stopwatch = Stopwatch.StartNew();
            foreach (var product in products)
            {
                await provider.SaveAsync(product.Id, product);
            }
            stopwatch.Stop();

            // Assert
            var throughput = recordCount / stopwatch.Elapsed.TotalSeconds;
            this.output.WriteLine($"Update {recordCount} records: {stopwatch.ElapsedMilliseconds}ms");
            this.output.WriteLine($"Throughput: {throughput:F2} updates/second");
        }

        [WindowsOnlyFact]
        public async Task Delete_Performance_Test()
        {
            // Arrange
            const int recordCount = 5000;
            using var provider = this.CreateProvider(nameof(Delete_Performance_Test));
            var products = GenerateProducts(recordCount);
            
            // Insert test data
            foreach (var product in products)
            {
                await provider.SaveAsync(product.Id, product);
            }

            // Act - Deletes
            var stopwatch = Stopwatch.StartNew();
            foreach (var product in products)
            {
                await provider.DeleteAsync(product.Id);
            }
            stopwatch.Stop();

            // Assert
            var throughput = recordCount / stopwatch.Elapsed.TotalSeconds;
            this.output.WriteLine($"Delete {recordCount} records: {stopwatch.ElapsedMilliseconds}ms");
            this.output.WriteLine($"Throughput: {throughput:F2} deletes/second");

            var count = await provider.CountAsync();
            count.Should().Be(0);
        }

        [WindowsOnlyFact]
        public async Task SessionPool_Performance_Comparison_Test()
        {
            // Test with session pool disabled
            const int operationCount = 1000;
            
            // Without session pool
            using (var providerNoPool = this.CreateProvider(nameof(SessionPool_Performance_Comparison_Test) + "_NoPool", useSessionPool: false))
            {
                var stopwatchNoPool = Stopwatch.StartNew();
                for (int i = 0; i < operationCount; i++)
                {
                    var product = new Product { Id = $"prod-{i}", Name = $"Product {i}", Quantity = i, Price = i * 10 };
                    await providerNoPool.SaveAsync(product.Id, product);
                    var result = await providerNoPool.GetAsync(product.Id);
                    result.Should().NotBeNull();
                }
                stopwatchNoPool.Stop();
                this.output.WriteLine($"Without session pool - {operationCount} operations: {stopwatchNoPool.ElapsedMilliseconds}ms");
            }

            // With session pool
            using (var providerWithPool = this.CreateProvider(nameof(SessionPool_Performance_Comparison_Test) + "_Pool", useSessionPool: true))
            {
                var stopwatchPool = Stopwatch.StartNew();
                for (int i = 0; i < operationCount; i++)
                {
                    var product = new Product { Id = $"prod-{i}", Name = $"Product {i}", Quantity = i, Price = i * 10 };
                    await providerWithPool.SaveAsync(product.Id, product);
                    var result = await providerWithPool.GetAsync(product.Id);
                    result.Should().NotBeNull();
                }
                stopwatchPool.Stop();
                this.output.WriteLine($"With session pool - {operationCount} operations: {stopwatchPool.ElapsedMilliseconds}ms");
            }
        }

        [WindowsOnlyFact]
        public async Task GetAll_Performance_Test()
        {
            // Arrange
            const int recordCount = 10000;
            using var provider = this.CreateProvider(nameof(GetAll_Performance_Test));
            var products = GenerateProducts(recordCount);
            
            // Insert test data
            foreach (var product in products)
            {
                await provider.SaveAsync(product.Id, product);
            }

            // Act - GetAll
            var stopwatch = Stopwatch.StartNew();
            var allProducts = await provider.GetAllAsync();
            stopwatch.Stop();

            // Assert
            allProducts.Count().Should().Be(recordCount);
            this.output.WriteLine($"GetAll {recordCount} records: {stopwatch.ElapsedMilliseconds}ms");
            this.output.WriteLine($"Throughput: {recordCount / stopwatch.Elapsed.TotalSeconds:F2} records/second");
        }

        [WindowsOnlyFact]
        public async Task GetAll_WithPredicate_Performance_Test()
        {
            // Arrange
            const int recordCount = 10000;
            using var provider = this.CreateProvider(nameof(GetAll_WithPredicate_Performance_Test));
            var products = GenerateProducts(recordCount);
            
            // Insert test data
            foreach (var product in products)
            {
                await provider.SaveAsync(product.Id, product);
            }

            // Act - GetAll with predicate
            var stopwatch = Stopwatch.StartNew();
            var filteredProducts = await provider.GetAllAsync(p => p.Quantity > 5000);
            stopwatch.Stop();

            // Assert
            var count = filteredProducts.Count();
            count.Should().BeGreaterThan(0).And.BeLessThan(recordCount);
            this.output.WriteLine($"GetAll with predicate from {recordCount} records returned {count} results: {stopwatch.ElapsedMilliseconds}ms");
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

        private static List<Product> GenerateProducts(int count)
        {
            return Enumerable.Range(1, count).Select(i => new Product
            {
                Id = $"product-{i:D6}",
                Name = $"Product {i}",
                Quantity = i,
                Price = i * 10.99m
            }).ToList();
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