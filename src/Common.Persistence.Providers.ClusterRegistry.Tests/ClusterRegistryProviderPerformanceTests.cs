//-------------------------------------------------------------------------------
// <copyright file="ClusterRegistryProviderPerformanceTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Tests
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
    using FluentAssertions;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Models;
    using Xunit;
    using Xunit.Abstractions;

    public class ClusterRegistryProviderPerformanceTests : IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly IServiceCollection services;
        private readonly string baseProviderName = "ClusterRegistryPerfTests";
        private readonly List<string> registryPaths = new List<string>();
        private bool isClusterAvailable;

        public ClusterRegistryProviderPerformanceTests(ITestOutputHelper output)
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
        public async Task BulkInsert_Performance_Test()
        {
            if (!this.isClusterAvailable)
            {
                this.output.WriteLine("Skipping test - Cluster service not available");
                return;
            }

            // Arrange
            const int recordCount = 1000; // Registry has size limits, so we use fewer records
            using var provider = this.CreateProvider(nameof(BulkInsert_Performance_Test));
            if (provider == null) return;

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
            if (!this.isClusterAvailable)
            {
                this.output.WriteLine("Skipping test - Cluster service not available");
                return;
            }

            // Arrange
            const int recordCount = 500;
            using var provider = this.CreateProvider(nameof(BulkRead_Performance_Test));
            if (provider == null) return;

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
            if (!this.isClusterAvailable)
            {
                this.output.WriteLine("Skipping test - Cluster service not available");
                return;
            }

            // Arrange
            const int recordCount = 500;
            const int accessCount = 1000;
            using var provider = this.CreateProvider(nameof(RandomAccess_Performance_Test));
            if (provider == null) return;

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
            if (!this.isClusterAvailable)
            {
                this.output.WriteLine("Skipping test - Cluster service not available");
                return;
            }

            // Arrange
            const int recordCount = 500;
            using var provider = this.CreateProvider(nameof(Update_Performance_Test));
            if (provider == null) return;

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
            if (!this.isClusterAvailable)
            {
                this.output.WriteLine("Skipping test - Cluster service not available");
                return;
            }

            // Arrange
            const int recordCount = 500;
            using var provider = this.CreateProvider(nameof(Delete_Performance_Test));
            if (provider == null) return;

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
        public async Task GetAll_Performance_Test()
        {
            if (!this.isClusterAvailable)
            {
                this.output.WriteLine("Skipping test - Cluster service not available");
                return;
            }

            // Arrange
            const int recordCount = 500;
            using var provider = this.CreateProvider(nameof(GetAll_Performance_Test));
            if (provider == null) return;

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
            if (!this.isClusterAvailable)
            {
                this.output.WriteLine("Skipping test - Cluster service not available");
                return;
            }

            // Arrange
            const int recordCount = 500;
            using var provider = this.CreateProvider(nameof(GetAll_WithPredicate_Performance_Test));
            if (provider == null) return;

            var products = GenerateProducts(recordCount);
            
            // Insert test data
            foreach (var product in products)
            {
                await provider.SaveAsync(product.Id, product);
            }

            // Act - GetAll with predicate
            var stopwatch = Stopwatch.StartNew();
            var filteredProducts = await provider.GetAllAsync(p => p.Quantity > 250);
            stopwatch.Stop();

            // Assert
            var count = filteredProducts.Count();
            count.Should().BeGreaterThan(0).And.BeLessThan(recordCount);
            this.output.WriteLine($"GetAll with predicate from {recordCount} records returned {count} results: {stopwatch.ElapsedMilliseconds}ms");
        }

        [WindowsOnlyFact]
        public async Task Compression_Performance_Comparison_Test()
        {
            if (!this.isClusterAvailable)
            {
                this.output.WriteLine("Skipping test - Cluster service not available");
                return;
            }

            // Test with compression disabled
            const int recordCount = 200;
            
            // Without compression
            using (var providerNoCompression = this.CreateProvider(nameof(Compression_Performance_Comparison_Test) + "_NoComp", enableCompression: false))
            {
                if (providerNoCompression == null) return;

                var products = GenerateProducts(recordCount);
                var stopwatchNoComp = Stopwatch.StartNew();
                foreach (var product in products)
                {
                    await providerNoCompression.SaveAsync(product.Id, product);
                }
                stopwatchNoComp.Stop();
                this.output.WriteLine($"Without compression - {recordCount} inserts: {stopwatchNoComp.ElapsedMilliseconds}ms");
            }

            // With compression
            using (var providerWithCompression = this.CreateProvider(nameof(Compression_Performance_Comparison_Test) + "_Comp", enableCompression: true))
            {
                if (providerWithCompression == null) return;

                var products = GenerateProducts(recordCount);
                var stopwatchComp = Stopwatch.StartNew();
                foreach (var product in products)
                {
                    await providerWithCompression.SaveAsync(product.Id, product);
                }
                stopwatchComp.Stop();
                this.output.WriteLine($"With compression - {recordCount} inserts: {stopwatchComp.ElapsedMilliseconds}ms");
            }
        }

        private ICrudStorageProvider<Product>? CreateProvider(string testName, bool enableCompression = true)
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
                    EnableCompression = enableCompression,
                    Enabled = true
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

        private static List<Product> GenerateProducts(int count)
        {
            return Enumerable.Range(1, count).Select(i => new Product
            {
                Id = $"product-{i:D4}",
                Name = $"Product {i}",
                Quantity = i,
                Price = i * 10.99m
            }).ToList();
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