//-------------------------------------------------------------------------------
// <copyright file="ClusterRegistryProviderIntegrationTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Tests
{
    using System;
    using System.Runtime.InteropServices;
    using System.Runtime.Versioning;
    using System.Threading.Tasks;
    using Common.Persistence.Configuration;
    using Common.Persistence.Factory;
    using AwesomeAssertions;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Models;
    using Xunit;

    /// <summary>
    /// Integration tests for ClusterRegistryProvider with fallback support.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class ClusterRegistryProviderIntegrationTests : IDisposable
    {
        private readonly string providerName = "TestRegistry";
        private readonly IServiceProvider serviceProvider;
        private readonly ClusterRegistryStoreSettings settings;

        public ClusterRegistryProviderIntegrationTests()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Setup
                var services = new ServiceCollection();
                services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
                var configuration = services.AddConfiguration();
                this.settings = configuration.GetConfiguredSettings<ClusterRegistryStoreSettings>($"Providers:{this.providerName}");
                services.AddKeyedSingleton<CrudStorageProviderSettings>(this.providerName, (_, _) => this.settings);
                services.AddPersistence();

                this.serviceProvider = services.BuildServiceProvider();

                // Clean up any existing test data
                this.CleanupTestData();
            }
        }

        [Fact]
        public async Task Provider_ShouldFallbackToLocalRegistry_WhenClusterNotAvailable()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return; // Skip on non-Windows platforms
            }

            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName);

            var product = new Product
            {
                Id = "test-product-1",
                Name = "Test Product",
                Quantity = 10,
                Price = 99.99m
            };

            // Act
            await provider.SaveAsync(product.Key, product);
            var retrieved = await provider.GetAsync(product.Key);

            // Assert
            retrieved.Should().NotBeNull();
            retrieved!.Id.Should().Be(product.Id);
            retrieved.Name.Should().Be(product.Name);
            retrieved.Quantity.Should().Be(product.Quantity);
            retrieved.Price.Should().Be(product.Price);

            // Verify data was written to local registry
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"{this.settings.RootPath}\{this.settings.ApplicationName}\{this.settings.ServiceName}\Product");
            key.Should().NotBeNull();
            var valueNames = key!.GetValueNames();
            valueNames.Should().ContainSingle();
        }

        [Fact]
        public async Task Provider_ShouldSupportAllCrudOperations_WithLocalRegistry()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return; // Skip on non-Windows platforms
            }

            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName);

            var product1 = new Product { Id = "crud-1", Name = "Product 1", Quantity = 5, Price = 10.50m };
            var product2 = new Product { Id = "crud-2", Name = "Product 2", Quantity = 3, Price = 20.75m };

            // Test Save
            await provider.SaveAsync(product1.Key, product1);
            await provider.SaveAsync(product2.Key, product2);

            // Test Get
            var retrieved1 = await provider.GetAsync(product1.Key);
            retrieved1.Should().NotBeNull();
            retrieved1!.Name.Should().Be(product1.Name);

            // Test Exists
            var exists = await provider.ExistsAsync(product2.Key);
            exists.Should().BeTrue();

            // Test GetAll
            var allProducts = await provider.GetAllAsync();
            allProducts.Should().HaveCountGreaterThanOrEqualTo(2);

            // Test Count
            var count = await provider.CountAsync();
            count.Should().BeGreaterThanOrEqualTo(2);

            // Test Delete
            await provider.DeleteAsync(product1.Key);
            var afterDelete = await provider.ExistsAsync(product1.Key);
            afterDelete.Should().BeFalse();

            // Test Clear
            var clearedCount = await provider.ClearAsync();
            clearedCount.Should().BeGreaterThanOrEqualTo(1);

            var afterClear = await provider.CountAsync();
            afterClear.Should().Be(0);
        }

        private void CleanupTestData()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    // Delete test registry key if it exists
                    Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(this.settings.RootPath, false);
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }
        }

        public void Dispose()
        {
            this.CleanupTestData();
        }
    }
}