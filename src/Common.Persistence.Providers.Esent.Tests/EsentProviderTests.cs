//-------------------------------------------------------------------------------
// <copyright file="EsentProviderTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.Esent.Tests
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using Common.Persistence.Configuration;
    using FluentAssertions;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Models;
    using Moq;
    using Xunit;

    public class EsentProviderTests : IDisposable
    {
        private readonly string testDatabasePath;
        private readonly string testDirectory;
        private readonly IServiceProvider serviceProvider;
        private readonly EsentProvider<Product> provider;

        public EsentProviderTests()
        {
            // Create a unique test directory for each test
            this.testDirectory = Path.Combine(Path.GetTempPath(), "EsentTests", Guid.NewGuid().ToString());
            this.testDatabasePath = Path.Combine(this.testDirectory, "test.db");
            Directory.CreateDirectory(this.testDirectory);

            // Setup dependency injection
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            
            // Mock configuration reader
            var mockConfigReader = new Mock<IConfigReader>();
            var settings = new EsentStoreSettings
            {
                DatabasePath = this.testDatabasePath,
                InstanceName = "TestInstance_" + Guid.NewGuid().ToString("N")[..8],
                CacheSizeMB = 16
            };
            mockConfigReader.Setup(x => x.ReadSettings<EsentStoreSettings>()).Returns(settings);
            services.AddSingleton(mockConfigReader.Object);

            // Mock serializer
            var mockSerializer = new Mock<Common.Persistence.Contract.ISerializer<Product>>();
            mockSerializer.Setup(x => x.SerializeAsync(It.IsAny<Product>(), default))
                .Returns<Product, System.Threading.CancellationToken>((product, ct) => 
                    Task.FromResult(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(product)));
            mockSerializer.Setup(x => x.DeserializeAsync(It.IsAny<byte[]>(), default))
                .Returns<byte[], System.Threading.CancellationToken>((data, ct) => 
                    Task.FromResult(System.Text.Json.JsonSerializer.Deserialize<Product>(data)));
            services.AddSingleton(mockSerializer.Object);

            this.serviceProvider = services.BuildServiceProvider();
            this.provider = new EsentProvider<Product>(this.serviceProvider, "Test");
        }

        [Fact]
        public async Task SaveAndGetAsync_ShouldWorkCorrectly()
        {
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

        [Fact]
        public async Task ExistsAsync_ShouldReturnTrueForExistingEntity()
        {
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

        [Fact]
        public async Task ExistsAsync_ShouldReturnFalseForNonExistentEntity()
        {
            // Act
            var exists = await this.provider.ExistsAsync("non-existent-id");

            // Assert
            exists.Should().BeFalse();
        }

        [Fact]
        public async Task DeleteAsync_ShouldRemoveEntity()
        {
            // Arrange
            var product = new Product
            {
                Id = "test-3",
                Name = "Test Product 3",
                Quantity = 3,
                Price = 29.99m
            };

            // Act
            await this.provider.SaveAsync(product.Key, product);
            var existsBeforeDelete = await this.provider.ExistsAsync(product.Key);
            
            await this.provider.DeleteAsync(product.Key);
            var existsAfterDelete = await this.provider.ExistsAsync(product.Key);

            // Assert
            existsBeforeDelete.Should().BeTrue();
            existsAfterDelete.Should().BeFalse();
        }

        [Fact]
        public async Task CountAsync_ShouldReturnCorrectCount()
        {
            // Arrange
            var product1 = new Product { Id = "count-1", Name = "Product 1", Quantity = 1, Price = 10m };
            var product2 = new Product { Id = "count-2", Name = "Product 2", Quantity = 2, Price = 20m };

            // Act
            await this.provider.SaveAsync(product1.Key, product1);
            await this.provider.SaveAsync(product2.Key, product2);
            
            var totalCount = await this.provider.CountAsync();
            var filteredCount = await this.provider.CountAsync(p => p.Price > 15m);

            // Assert
            totalCount.Should().BeGreaterThanOrEqualTo(2);
            filteredCount.Should().BeGreaterThanOrEqualTo(1);
        }

        [Fact]
        public async Task GetAllAsync_ShouldReturnAllEntities()
        {
            // Arrange
            var product1 = new Product { Id = "all-1", Name = "Product A", Quantity = 1, Price = 10m };
            var product2 = new Product { Id = "all-2", Name = "Product B", Quantity = 2, Price = 20m };

            // Act
            await this.provider.SaveAsync(product1.Key, product1);
            await this.provider.SaveAsync(product2.Key, product2);
            
            var allProducts = await this.provider.GetAllAsync(p => p.Quantity > 0);

            // Assert
            allProducts.Should().HaveCountGreaterThanOrEqualTo(2);
            allProducts.Should().Contain(p => p.Key == product1.Key);
            allProducts.Should().Contain(p => p.Key == product2.Key);
        }

        public void Dispose()
        {
            this.provider?.Dispose();
            this.serviceProvider?.GetService<IDisposable>()?.Dispose();
            
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