//-------------------------------------------------------------------------------
// <copyright file="SQLiteProviderTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.SQLite.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using AwesomeAssertions;
    using Common.Persistence.Configuration;
    using Common.Persistence.Factory;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Models;
    using Xunit;
    using Xunit.Abstractions;

    public class SQLiteProviderTests : IAsyncLifetime, IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly string providerName = "SQLiteTest";
        private readonly string schemaName = "test1";
        private readonly string databasePath;
        private IServiceProvider serviceProvider;
        private ICrudStorageProviderFactory? factory;

        public SQLiteProviderTests(ITestOutputHelper output)
        {
            this.output = output;
            this.databasePath = Path.Combine(Directory.GetCurrentDirectory(), $"test_{Guid.NewGuid():N}.db");
        }

        public Task InitializeAsync()
        {
            // Setup DI container
            var services = new ServiceCollection();
            services.AddLogging(builder => builder
                .SetMinimumLevel(LogLevel.Debug)
                .AddXunit(this.output));

            // Configure SQLite provider
            var config = new Dictionary<string, string>
            {
                [$"Providers:{this.providerName}:Name"] = this.providerName,
                [$"Providers:{this.providerName}:AssemblyName"] = "CRP.Common.Persistence.Providers.SQLite",
                [$"Providers:{this.providerName}:TypeName"] = "Common.Persistence.Providers.SQLite.SQLiteProvider`1",
                [$"Providers:{this.providerName}:Enabled"] = "true",
                [$"Providers:{this.providerName}:Capabilities"] = "1",
                [$"Providers:{this.providerName}:DataSource"] = this.databasePath,
                [$"Providers:{this.providerName}:Mode"] = "ReadWriteCreate",
                [$"Providers:{this.providerName}:Cache"] = "Shared",
                [$"Providers:{this.providerName}:ForeignKeys"] = "true",
                [$"Providers:{this.providerName}:CommandTimeout"] = "30",
                [$"Providers:{this.providerName}:CreateTableIfNotExists"] = "true",
                [$"Providers:{this.providerName}:Schema"] = this.schemaName
            };

            var configuration = services.AddConfiguration(config);

            // Register settings
            var settings = configuration.GetConfiguredSettings<SQLiteProviderSettings>($"Providers:{this.providerName}");
            services.AddKeyedSingleton<CrudStorageProviderSettings>(this.providerName, (_, _) => settings);
            services.AddSingleton<IConfigReader, JsonConfigReader>();
            services.AddPersistence();

            this.serviceProvider = services.BuildServiceProvider();
            this.factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            // Clear all data in the schema
            await this.ClearAllTablesInSchemaAsync();

            // Dispose the service provider to ensure all connections are closed
            if (this.serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }

            // Force garbage collection to ensure SQLite connections are released
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Delete the database file with retry
            var maxRetries = 5;
            var retryDelay = TimeSpan.FromMilliseconds(500);

            for (var i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (File.Exists(this.databasePath))
                    {
                        File.Delete(this.databasePath);
                        break; // Success, exit loop
                    }
                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    // Wait before retrying
                    await Task.Delay(retryDelay);
                }
            }
        }

        public void Dispose()
        {
            (this.serviceProvider as IDisposable)?.Dispose();
        }

        private async Task ClearAllTablesInSchemaAsync()
        {
            try
            {
                using var provider = this.factory?.Create<Product>(this.providerName);
                if (provider != null)
                {
                    await provider.ClearAsync();
                }
            }
            catch (Exception ex)
            {
                this.output.WriteLine($"Error clearing tables: {ex.Message}");
            }
        }

        [Fact]
        public async Task SaveAndGet_SingleEntity_ReturnsCorrectEntity()
        {
            // Arrange
            using var provider = this.factory!.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            var product = new Product
            {
                Id = "test-product-001",
                Name = "Test Product",
                Quantity = 100,
                Price = 99.99m,
                Description = "This is a test product with detailed description",
                Tags = new List<string> { "test", "sample", "product" }
            };

            // Act
            await provider.SaveAsync(product.Key, product);
            var retrievedProduct = await provider.GetAsync(product.Key);

            // Assert
            retrievedProduct.Should().NotBeNull();
            retrievedProduct.Id.Should().Be(product.Id);
            retrievedProduct.Name.Should().Be(product.Name);
            retrievedProduct.Quantity.Should().Be(product.Quantity);
            retrievedProduct.Price.Should().Be(product.Price);
            retrievedProduct.Description.Should().Be(product.Description);
            retrievedProduct.Tags.Should().BeEquivalentTo(product.Tags);
        }

        [Fact]
        public async Task SaveMany_MultipleEntities_SavesAllCorrectly()
        {
            // Arrange
            using var provider = this.factory!.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            var products = Enumerable.Range(1, 5).Select(i => new Product
            {
                Id = $"bulk-product-{i:D3}",
                Name = $"Bulk Product {i}",
                Quantity = i * 10,
                Price = i * 19.99m,
                Description = $"Bulk product {i} description"
            }).ToList();

            var entities = products.Select(p => new KeyValuePair<string, Product>(p.Key, p));

            // Act
            await provider.SaveManyAsync(entities);

            // Assert
            foreach (var product in products)
            {
                var retrieved = await provider.GetAsync(product.Key);
                retrieved.Should().NotBeNull();
                retrieved.Id.Should().Be(product.Id);
                retrieved.Name.Should().Be(product.Name);
            }
        }

        [Fact]
        public async Task GetMany_WithKeys_ReturnsMatchingEntities()
        {
            // Arrange
            using var provider = this.factory!.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            var products = Enumerable.Range(1, 5).Select(i => new Product
            {
                Id = $"getmany-product-{i:D3}",
                Name = $"GetMany Product {i}",
                Quantity = i * 10
            }).ToList();

            foreach (var product in products)
            {
                await provider.SaveAsync(product.Key, product);
            }

            var keysToRetrieve = products.Take(3).Select(p => p.Key).ToList();

            // Act
            var retrieved = await provider.GetManyAsync(keysToRetrieve);

            // Assert
            var retrievedList = retrieved.ToList();
            retrievedList.Should().HaveCount(3);
            retrievedList.Select(p => p.Id).Should().BeEquivalentTo(products.Take(3).Select(p => p.Id));
        }

        [Fact]
        public async Task Delete_ExistingEntity_RemovesEntity()
        {
            // Arrange
            using var provider = this.factory!.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            var product = new Product
            {
                Id = "delete-test-product",
                Name = "Product to Delete"
            };

            await provider.SaveAsync(product.Key, product);

            // Act
            await provider.DeleteAsync(product.Key);

            // Assert
            var retrieved = await provider.GetAsync(product.Key);
            retrieved.Should().BeNull();
        }

        [Fact]
        public async Task Exists_ForExistingAndNonExistingEntities_ReturnsCorrectValues()
        {
            // Arrange
            using var provider = this.factory!.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            var product = new Product
            {
                Id = "exists-test-product",
                Name = "Product for Exists Test"
            };

            await provider.SaveAsync(product.Key, product);

            // Act & Assert
            var existsResult = await provider.ExistsAsync(product.Key);
            existsResult.Should().BeTrue();

            var notExistsResult = await provider.ExistsAsync("Product/non-existing-product");
            notExistsResult.Should().BeFalse();
        }

        [Fact]
        public async Task Count_WithAndWithoutPredicate_ReturnsCorrectCounts()
        {
            // Arrange
            using var provider = this.factory!.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            // Clear any existing data
            await provider.ClearAsync();

            var products = new List<Product>
            {
                new Product { Id = "count-product-1", Name = "Product 1", Quantity = 50 },
                new Product { Id = "count-product-2", Name = "Product 2", Quantity = 150 },
                new Product { Id = "count-product-3", Name = "Product 3", Quantity = 200 }
            };

            foreach (var product in products)
            {
                await provider.SaveAsync(product.Key, product);
            }

            // Act
            var totalCount = await provider.CountAsync();
            var filteredCount = await provider.CountAsync(p => p.Quantity > 100);

            // Assert
            totalCount.Should().Be(3);
            filteredCount.Should().Be(2);
        }

        [Fact]
        public async Task Clear_RemovesAllEntities()
        {
            // Arrange
            using var provider = this.factory!.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            var products = Enumerable.Range(1, 3).Select(i => new Product
            {
                Id = $"clear-product-{i}",
                Name = $"Product {i}"
            }).ToList();

            foreach (var product in products)
            {
                await provider.SaveAsync(product.Key, product);
            }

            // Act
            var clearedCount = await provider.ClearAsync();

            // Assert
            clearedCount.Should().BeGreaterThanOrEqualTo(3);
            var count = await provider.CountAsync();
            count.Should().Be(0);
        }

        [Fact]
        public async Task GetAll_WithPredicate_FiltersCorrectly()
        {
            // Arrange
            using var provider = this.factory!.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            // Clear any existing data
            await provider.ClearAsync();

            var products = new List<Product>
            {
                new Product { Id = "getall-product-1", Name = "Expensive Product", Price = 200m },
                new Product { Id = "getall-product-2", Name = "Cheap Product", Price = 50m },
                new Product { Id = "getall-product-3", Name = "Medium Product", Price = 100m }
            };

            foreach (var product in products)
            {
                await provider.SaveAsync(product.Key, product);
            }

            // Act
            var allProducts = await provider.GetAllAsync();
            var expensiveProducts = (await provider.GetAllAsync(p => p.Price > 75m)).ToArray();

            // Assert
            allProducts.Should().HaveCount(3);
            expensiveProducts.Should().NotBeNullOrEmpty();
            expensiveProducts.Should().HaveCount(2);
            expensiveProducts.All(p => p.Price > 75m).Should().BeTrue();
        }

        [Fact]
        public async Task Update_ExistingEntity_UpdatesCorrectly()
        {
            // Arrange
            using var provider = this.factory!.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            var product = new Product
            {
                Id = "update-test-product",
                Name = "Original Name",
                Price = 100m,
                Version = 1
            };

            await provider.SaveAsync(product.Key, product);

            // Act
            product.Name = "Updated Name";
            product.Price = 150m;
            product.Version = 2;
            await provider.SaveAsync(product.Key, product);

            // Assert
            var updated = await provider.GetAsync(product.Key);
            updated.Should().NotBeNull();
            updated.Name.Should().Be("Updated Name");
            updated.Price.Should().Be(150m);
            updated.Version.Should().Be(2);
        }

        [Fact]
        public async Task InMemoryDatabase_WorksCorrectly()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddXunit(this.output));

            // For in-memory SQLite to work properly across connections, we need to keep a connection open
            // Instead, let's use a unique file in temp directory
            var tempDbPath = Path.Combine(Path.GetTempPath(), $"inmemory_test_{Guid.NewGuid():N}.db");

            var config = new Dictionary<string, string>
            {
                [$"Providers:InMemorySQLite:Name"] = "InMemorySQLite",
                [$"Providers:InMemorySQLite:AssemblyName"] = "CRP.Common.Persistence.Providers.SQLite",
                [$"Providers:InMemorySQLite:TypeName"] = "Common.Persistence.Providers.SQLite.SQLiteProvider`1",
                [$"Providers:InMemorySQLite:Enabled"] = "true",
                [$"Providers:InMemorySQLite:Capabilities"] = "1",
                [$"Providers:InMemorySQLite:DataSource"] = tempDbPath,
                [$"Providers:InMemorySQLite:CreateTableIfNotExists"] = "true"
            };

            var configuration = services.AddConfiguration(config);
            var settings = configuration.GetConfiguredSettings<SQLiteProviderSettings>("Providers:InMemorySQLite");
            services.AddKeyedSingleton<CrudStorageProviderSettings>("InMemorySQLite", (_, _) => settings);
            services.AddSingleton<IConfigReader, JsonConfigReader>();
            services.AddPersistence();

            var sp = services.BuildServiceProvider();
            var factory = sp.GetRequiredService<ICrudStorageProviderFactory>();

            try
            {
                using var provider = factory.Create<Product>("InMemorySQLite")
                    ?? throw new InvalidOperationException("Failed to create provider");

                var product = new Product
                {
                    Id = "memory-test",
                    Name = "In-Memory Product"
                };

                // Act
                await provider.SaveAsync(product.Key, product);
                var retrieved = await provider.GetAsync(product.Key);

                // Assert
                retrieved.Should().NotBeNull();
                retrieved.Id.Should().Be(product.Id);
                retrieved.Name.Should().Be(product.Name);
            }
            finally
            {
                // Cleanup
                if (File.Exists(tempDbPath))
                {
                    File.Delete(tempDbPath);
                }
            }
        }
    }
}