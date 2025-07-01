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
    using AwesomeAssertions;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Models;
    using Xunit;
    using Xunit.Abstractions;

    public class ClusterRegistryProviderPerformanceTests : IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly IServiceCollection services;
        private readonly string providerName = "ClusterRegistryPerfTests";
        private readonly ClusterRegistryStoreSettings settings;
        private readonly List<string> testNames = new List<string>();
        private readonly Dictionary<string, IServiceProvider> testServiceProviders = new Dictionary<string, IServiceProvider>();

        public ClusterRegistryProviderPerformanceTests(ITestOutputHelper output)
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
        public async Task BulkInsert_Performance_Test()
        {
            // Arrange
            const int recordCount = 10000; // Registry has size limits, so we use fewer records
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
        public async Task GetAll_Performance_Test()
        {
            // Arrange
            const int recordCount = 5000;
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
            const int recordCount = 5000;
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
            var filteredProducts = await provider.GetAllAsync(p => p.Quantity > 250);
            stopwatch.Stop();

            // Assert
            var count = filteredProducts.Count();
            count.Should().BeGreaterThan(0).And.BeLessThan(recordCount);
            this.output.WriteLine($"GetAll with predicate from {recordCount} records returned {count} results: {stopwatch.ElapsedMilliseconds}ms");

            // Cleanup test data immediately
            this.CleanupTestData(testName);
        }

        [WindowsOnlyFact]
        public async Task Compression_Performance_Comparison_Test()
        {
            // Test with compression disabled
            const int recordCount = 5000;
            var testNameNoComp = nameof(Compression_Performance_Comparison_Test) + "_NoComp";
            var testNameComp = nameof(Compression_Performance_Comparison_Test) + "_Comp";
            this.testNames.Add(testNameNoComp);
            this.testNames.Add(testNameComp);
            this.CleanupTestData(testNameNoComp);
            this.CleanupTestData(testNameComp);
            
            // Without compression
            using (var providerNoCompression = this.CreateProvider(testNameNoComp, enableCompression: false) ?? throw new InvalidOperationException($"Failed to create provider for test {testNameNoComp}"))
            {

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
            using (var providerWithCompression = this.CreateProvider(testNameComp, enableCompression: true) ?? throw new InvalidOperationException($"Failed to create provider for test {testNameComp}"))
            {

                var products = GenerateProducts(recordCount);
                var stopwatchComp = Stopwatch.StartNew();
                foreach (var product in products)
                {
                    await providerWithCompression.SaveAsync(product.Id, product);
                }
                stopwatchComp.Stop();
                this.output.WriteLine($"With compression - {recordCount} inserts: {stopwatchComp.ElapsedMilliseconds}ms");
            }

            // Cleanup test data immediately
            this.CleanupTestData(testNameNoComp);
            this.CleanupTestData(testNameComp);
        }

        private ICrudStorageProvider<Product>? CreateProvider(string testName, bool enableCompression = true)
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
                        EnableCompression = enableCompression,
                        MaxValueSizeKB = this.settings.MaxValueSizeKB,
                        ConnectionTimeoutSeconds = this.settings.ConnectionTimeoutSeconds,
                        RetryCount = this.settings.RetryCount,
                        RetryDelayMilliseconds = this.settings.RetryDelayMilliseconds
                    };
                    
                    var keyPrefix = $"Providers:{testName}";
                    // Create in-memory configuration for testSettings
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
    }
}