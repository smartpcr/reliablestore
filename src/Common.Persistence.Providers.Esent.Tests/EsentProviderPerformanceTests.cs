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
        private readonly string providerName = "EsentProviderPerfTests";
        private readonly List<string> testNames = new List<string>();
        private readonly Dictionary<string, IServiceProvider> testServiceProviders = new Dictionary<string, IServiceProvider>();
        private readonly Dictionary<string, string> testDatabasePaths = new Dictionary<string, string>();

        public EsentProviderPerformanceTests(ITestOutputHelper output)
        {
            this.output = output;

            // Skip initialization on non-Windows platforms
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            // No shared initialization needed - each test creates its own service provider
        }

        [WindowsOnlyFact]
        public async Task BulkInsert_Performance_Test()
        {
            // Arrange
            const int recordCount = 10000;
            var testName = nameof(BulkInsert_Performance_Test);
            this.testNames.Add(testName);
            this.CleanupTestData(testName); // Clean up any existing data before test
            using var provider = this.CreateProvider(testName) ?? throw new InvalidOperationException($"Failed to create provider for test {testName}");
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

            // Cleanup test data immediately
            this.CleanupTestData(testName);
        }

        [WindowsOnlyFact]
        public async Task BulkRead_Performance_Test()
        {
            // Arrange
            const int recordCount = 5000;
            var testName = nameof(BulkRead_Performance_Test);
            this.testNames.Add(testName);
            this.CleanupTestData(testName); // Clean up any existing data before test
            using var provider = this.CreateProvider(testName) ?? throw new InvalidOperationException($"Failed to create provider for test {testName}");
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

            // Cleanup test data immediately
            this.CleanupTestData(testName);
        }

        [WindowsOnlyFact]
        public async Task RandomAccess_Performance_Test()
        {
            // Arrange
            const int recordCount = 5000;
            const int accessCount = 10000;
            var testName = nameof(RandomAccess_Performance_Test);
            this.testNames.Add(testName);
            this.CleanupTestData(testName); // Clean up any existing data before test
            using var provider = this.CreateProvider(testName) ?? throw new InvalidOperationException($"Failed to create provider for test {testName}");
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

            // Cleanup test data immediately
            this.CleanupTestData(testName);
        }

        [WindowsOnlyFact]
        public async Task Update_Performance_Test()
        {
            // Arrange
            const int recordCount = 5000;
            var testName = nameof(Update_Performance_Test);
            this.testNames.Add(testName);
            this.CleanupTestData(testName); // Clean up any existing data before test
            using var provider = this.CreateProvider(testName) ?? throw new InvalidOperationException($"Failed to create provider for test {testName}");
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

            // Cleanup test data immediately
            this.CleanupTestData(testName);
        }

        [WindowsOnlyFact]
        public async Task Delete_Performance_Test()
        {
            // Arrange
            const int recordCount = 5000;
            var testName = nameof(Delete_Performance_Test);
            this.testNames.Add(testName);
            this.CleanupTestData(testName); // Clean up any existing data before test
            using var provider = this.CreateProvider(testName) ?? throw new InvalidOperationException($"Failed to create provider for test {testName}");
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

            // Cleanup test data immediately
            this.CleanupTestData(testName);
        }

        [WindowsOnlyFact]
        public async Task SessionPool_Performance_Comparison_Test()
        {
            // Test with session pool disabled
            const int operationCount = 1000;
            var testNameNoPool = nameof(SessionPool_Performance_Comparison_Test) + "_NoPool";
            var testNamePool = nameof(SessionPool_Performance_Comparison_Test) + "_Pool";
            this.testNames.Add(testNameNoPool);
            this.testNames.Add(testNamePool);
            this.CleanupTestData(testNameNoPool);
            this.CleanupTestData(testNamePool);
            
            // Without session pool
            using (var providerNoPool = this.CreateProvider(testNameNoPool, useSessionPool: false) ?? throw new InvalidOperationException($"Failed to create provider for test {testNameNoPool}"))
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
            using (var providerWithPool = this.CreateProvider(testNamePool, useSessionPool: true) ?? throw new InvalidOperationException($"Failed to create provider for test {testNamePool}"))
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

            // Cleanup test data immediately
            this.CleanupTestData(testNameNoPool);
            this.CleanupTestData(testNamePool);
        }

        [WindowsOnlyFact]
        public async Task GetAll_Performance_Test()
        {
            // Arrange
            const int recordCount = 10000;
            var testName = nameof(GetAll_Performance_Test);
            this.testNames.Add(testName);
            this.CleanupTestData(testName); // Clean up any existing data before test
            using var provider = this.CreateProvider(testName) ?? throw new InvalidOperationException($"Failed to create provider for test {testName}");
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

            // Cleanup test data immediately
            this.CleanupTestData(testName);
        }

        [WindowsOnlyFact]
        public async Task GetAll_WithPredicate_Performance_Test()
        {
            // Arrange
            const int recordCount = 10000;
            var testName = nameof(GetAll_WithPredicate_Performance_Test);
            this.testNames.Add(testName);
            this.CleanupTestData(testName); // Clean up any existing data before test
            using var provider = this.CreateProvider(testName) ?? throw new InvalidOperationException($"Failed to create provider for test {testName}");
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

            // Cleanup test data immediately
            this.CleanupTestData(testName);
        }

        private ICrudStorageProvider<Product>? CreateProvider(string testName, bool useSessionPool = false)
        {
            try
            {
                // Reuse service provider for the same test name if it exists
                if (!this.testServiceProviders.TryGetValue(testName, out var serviceProvider))
                {
                    // Create a test-specific configuration to isolate data
                    var testSpecificServices = new ServiceCollection();
                    testSpecificServices.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
                    
                    // Create unique database path for this test
                    var dbPath = $"data/test_{testName}_{Guid.NewGuid():N}.db";
                    this.testDatabasePaths[testName] = dbPath;
                    
                    // Create test-specific settings
                    var testSettings = new EsentStoreSettings
                    {
                        Name = this.providerName,
                        DatabasePath = dbPath,
                        InstanceName = $"TestInstance_{testName}",
                        UseSessionPool = useSessionPool,
                        Enabled = true,
                        MaxDatabaseSizeMB = 1024,
                        CacheSizeMB = 64,
                        EnableVersioning = true,
                        PageSizeKB = 8
                    };
                    
                    var keyPrefix = $"Providers:{testName}";
                    // Create in-memory configuration for testSettings
                    testSpecificServices.AddConfiguration(new Dictionary<string, string>
                    {
                        [$"{keyPrefix}:Name"] = testSettings.Name,
                        [$"{keyPrefix}:DatabasePath"] = testSettings.DatabasePath,
                        [$"{keyPrefix}:InstanceName"] = testSettings.InstanceName,
                        [$"{keyPrefix}:UseSessionPool"] = testSettings.UseSessionPool.ToString(),
                        [$"{keyPrefix}:Enabled"] = testSettings.Enabled.ToString(),
                        [$"{keyPrefix}:MaxDatabaseSizeMB"] = testSettings.MaxDatabaseSizeMB.ToString(),
                        [$"{keyPrefix}:CacheSizeMB"] = testSettings.CacheSizeMB.ToString(),
                        [$"{keyPrefix}:EnableVersioning"] = testSettings.EnableVersioning.ToString(),
                        [$"{keyPrefix}:PageSizeKB"] = testSettings.PageSizeKB.ToString()
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

        private void CleanupTestData(string testName)
        {
            try
            {
                if (this.testDatabasePaths.TryGetValue(testName, out var dbPath))
                {
                    // Ensure directory exists
                    var directory = System.IO.Path.GetDirectoryName(dbPath);
                    if (!System.IO.Directory.Exists(directory))
                    {
                        System.IO.Directory.CreateDirectory(directory!);
                    }
                    
                    // Delete database file
                    if (System.IO.File.Exists(dbPath))
                    {
                        System.IO.File.Delete(dbPath);
                    }
                    
                    // Clean up ESENT log files and checkpoint files
                    if (System.IO.Directory.Exists(directory))
                    {
                        // Delete ESENT log files
                        foreach (var file in System.IO.Directory.GetFiles(directory, "edb*.log"))
                        {
                            try { System.IO.File.Delete(file); } catch { }
                        }
                        // Delete checkpoint files
                        foreach (var file in System.IO.Directory.GetFiles(directory, "*.chk"))
                        {
                            try { System.IO.File.Delete(file); } catch { }
                        }
                        // Delete temp files
                        foreach (var file in System.IO.Directory.GetFiles(directory, "*.tmp"))
                        {
                            try { System.IO.File.Delete(file); } catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.output?.WriteLine($"Error cleaning up test data for {testName}: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Clean up all test data
                foreach (var testName in this.testNames)
                {
                    this.CleanupTestData(testName);
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
        }
    }
}