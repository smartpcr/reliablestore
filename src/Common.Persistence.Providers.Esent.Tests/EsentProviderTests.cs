//-------------------------------------------------------------------------------
// <copyright file="EsentProviderTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.Esent.Tests
{
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;
    using Common.Persistence.Configuration;
    using Common.Persistence.Contract;
    using Common.Persistence.Factory;
    using FluentAssertions;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Models;

    public class EsentProviderTests
    {
        private readonly string providerName = "EsentProviderTests";
        private readonly IServiceCollection services;

        public EsentProviderTests()
        {
            // Skip initialization on non-Windows platforms
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            // Setup dependency injection
            this.services = new ServiceCollection();
            this.services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            var configuration = this.services.AddConfiguration();
            var settings = configuration.GetConfiguredSettings<EsentStoreSettings>(this.providerName);
            this.services.AddKeyedScoped<CrudStorageProviderSettings>(this.providerName, (_, _) => settings);
            this.services.AddPersistence();
        }

        [WindowsOnlyFact]
        public async Task SaveAndGetAsync_ShouldWorkCorrectly()
        {
            // Arrange
            using var provider = this.ResetInstanceName(nameof(this.SaveAndGetAsync_ShouldWorkCorrectly));
            var product = new Product
            {
                Id = "test-1",
                Name = "Test Product",
                Quantity = 10,
                Price = 99.99m
            };

            // Act
            await provider.SaveAsync(product.Key, product);
            var retrieved = await provider.GetAsync(product.Key);

            // Assert
            retrieved.Should().NotBeNull();
            retrieved!.Key.Should().Be(product.Key);
            retrieved.Quantity.Should().Be(product.Quantity);
            retrieved.Price.Should().Be(product.Price);
        }

        [WindowsOnlyFact]
        public async Task ExistsAsync_ShouldReturnTrueForExistingEntity()
        {
            // Arrange
            using var provider = this.ResetInstanceName(nameof(this.ExistsAsync_ShouldReturnTrueForExistingEntity));
            var product = new Product
            {
                Id = "test-2",
                Name = "Test Product 2",
                Quantity = 5,
                Price = 49.99m
            };

            // Act
            await provider.SaveAsync(product.Key, product);
            var exists = await provider.ExistsAsync(product.Key);

            // Assert
            exists.Should().BeTrue();
        }

        [WindowsOnlyFact]
        public async Task ExistsAsync_ShouldReturnFalseForNonExistentEntity()
        {
            using var provider = this.ResetInstanceName(nameof(this.ExistsAsync_ShouldReturnFalseForNonExistentEntity));
            var exists = await provider.ExistsAsync("non-existent-id");

            // Assert
            exists.Should().BeFalse();
        }

        [WindowsOnlyFact]
        public async Task DeleteAsync_ShouldRemoveEntity()
        {
            using var provider = this.ResetInstanceName(nameof(this.DeleteAsync_ShouldRemoveEntity));
            // Arrange
            var product = new Product
            {
                Id = "test-3",
                Name = "Test Product 3",
                Quantity = 3,
                Price = 29.99m
            };

            // Act
            await provider.SaveAsync(product.Key, product);
            var existsBeforeDelete = await provider.ExistsAsync(product.Key);

            await provider.DeleteAsync(product.Key);
            var existsAfterDelete = await provider.ExistsAsync(product.Key);

            // Assert
            existsBeforeDelete.Should().BeTrue();
            existsAfterDelete.Should().BeFalse();
        }

        [WindowsOnlyFact]
        public async Task CountAsync_ShouldReturnCorrectCount()
        {
            using var provider = this.ResetInstanceName(nameof(this.CountAsync_ShouldReturnCorrectCount));
            // Arrange
            var product1 = new Product { Id = "count-1", Name = "Product 1", Quantity = 1, Price = 10m };
            var product2 = new Product { Id = "count-2", Name = "Product 2", Quantity = 2, Price = 20m };

            // Act
            await provider.SaveAsync(product1.Key, product1);
            await provider.SaveAsync(product2.Key, product2);

            var totalCount = await provider.CountAsync();
            var filteredCount = await provider.CountAsync(p => p.Price > 15m);

            // Assert
            totalCount.Should().BeGreaterThanOrEqualTo(2);
            filteredCount.Should().BeGreaterThanOrEqualTo(1);
        }

        [WindowsOnlyFact]
        public async Task GetAllAsync_ShouldReturnAllEntities()
        {
            using  var provider = this.ResetInstanceName(nameof(this.GetAllAsync_ShouldReturnAllEntities));
            // Arrange
            var product1 = new Product { Id = "all-1", Name = "Product A", Quantity = 1, Price = 10m };
            var product2 = new Product { Id = "all-2", Name = "Product B", Quantity = 2, Price = 20m };

            // Act
            await provider.SaveAsync(product1.Key, product1);
            await provider.SaveAsync(product2.Key, product2);

            var allProducts = await provider.GetAllAsync(p => p.Quantity > 0);

            // Assert
            allProducts.Should().HaveCountGreaterThanOrEqualTo(2);
            allProducts.Should().Contain(p => p.Key == product1.Key);
            allProducts.Should().Contain(p => p.Key == product2.Key);
        }

        private ICrudStorageProvider<Product> ResetInstanceName(string instanceName)
        {
            var sp = this.services.BuildServiceProvider();
            var configReader = sp.GetRequiredService<IConfigReader>();
            var settings = configReader.ReadSettings<EsentStoreSettings>(this.providerName);
            settings.InstanceName = instanceName;

            var factory = sp.GetRequiredService<ICrudStorageProviderFactory>();
            var provider = factory.Create<Product>(this.providerName);
            return provider;
        }
    }
}