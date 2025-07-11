//-------------------------------------------------------------------------------
// <copyright file="EsentProviderSimpleTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.Esent.Tests
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;
    using AwesomeAssertions;
    using Models;
    using Xunit;

    public class EsentProviderSimpleTests : IDisposable
    {
        private readonly string testDatabasePath;
        private readonly string testDirectory;
        private readonly SimpleEsentProvider<Product> provider;

        public EsentProviderSimpleTests()
        {
            // Skip initialization on non-Windows platforms
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            // Create a unique test directory for each test
            this.testDirectory = Path.Combine(Path.GetTempPath(), "EsentTests", Guid.NewGuid().ToString());
            this.testDatabasePath = Path.Combine(this.testDirectory, "test.db");
            Directory.CreateDirectory(this.testDirectory);

            var settings = new EsentStoreSettings
            {
                DatabasePath = this.testDatabasePath,
                InstanceName = "TestInstance_" + Guid.NewGuid().ToString("N")[..8],
                CacheSizeMB = 16
            };

            this.provider = new SimpleEsentProvider<Product>(settings);
        }

        [WindowsOnlyFact]
        public async Task SaveAndGetAsync_ShouldWorkCorrectly()
        {
            if (this.provider == null) return; // Skip if not initialized
            // Arrange
            var product = new Product
            {
                Id = "test-1",
                Name = "Test Product",
                Quantity = 10,
                Price = 99.99m
            };

            // Act
            await this.provider.SaveAsync(product.Key, product);
            var retrieved = await this.provider.GetAsync(product.Key);

            // Assert
            retrieved.Should().NotBeNull();
            retrieved!.Key.Should().Be(product.Key);
            retrieved.Quantity.Should().Be(product.Quantity);
            retrieved.Price.Should().Be(product.Price);
        }

        [WindowsOnlyFact]
        public async Task ExistsAsync_ShouldReturnTrueForExistingEntity()
        {
            if (this.provider == null) return; // Skip if not initialized
            // Arrange
            var product = new Product
            {
                Id = "test-2",
                Name = "Test Product 2",
                Quantity = 5,
                Price = 49.99m
            };

            // Act
            await this.provider.SaveAsync(product.Key, product);
            var exists = await this.provider.ExistsAsync(product.Key);

            // Assert
            exists.Should().BeTrue();
        }

        [WindowsOnlyFact]
        public async Task ExistsAsync_ShouldReturnFalseForNonExistentEntity()
        {
            if (this.provider == null) return; // Skip if not initialized
            // Act
            var exists = await this.provider.ExistsAsync("non-existent-id");

            // Assert
            exists.Should().BeFalse();
        }

        public void Dispose()
        {
            this.provider?.Dispose();

            // Clean up test files
            if (Directory.Exists(this.testDirectory))
            {
                try
                {
                    Directory.Delete(this.testDirectory, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
}