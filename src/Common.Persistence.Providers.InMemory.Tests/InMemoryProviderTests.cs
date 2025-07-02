//-------------------------------------------------------------------------------
// <copyright file="InMemoryProviderTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.InMemory.Tests
{
    using System;
    using System.Collections.Generic;
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

    public class InMemoryProviderTests : IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly IServiceProvider serviceProvider;
        private readonly string providerName = "InMemoryTest";

        public InMemoryProviderTests(ITestOutputHelper output)
        {
            this.output = output;

            // Setup DI container
            var services = new ServiceCollection();
            services.AddLogging(builder => builder
                .SetMinimumLevel(LogLevel.Debug));

            // Configure InMemory provider
            var config = new Dictionary<string, string>
            {
                [$"Providers:{this.providerName}:Name"] = this.providerName,
                [$"Providers:{this.providerName}:Enabled"] = "true"
            };

            var configuration = services.AddConfiguration(config);
            
            // Register settings
            var settings = configuration.GetConfiguredSettings<InMemoryStoreSettings>($"Providers:{this.providerName}");
            services.AddKeyedSingleton<CrudStorageProviderSettings>(this.providerName, (_, _) => settings);
            
            services.AddPersistence();

            this.serviceProvider = services.BuildServiceProvider();
        }

        public void Dispose()
        {
            if (this.serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        [Fact]
        public async Task SaveAndGet_SingleEntity_ReturnsCorrectEntity()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
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
            await provider.SaveAsync(product.Id, product);
            var retrieved = await provider.GetAsync(product.Id);

            // Assert
            retrieved.Should().NotBeNull();
            retrieved!.Id.Should().Be(product.Id);
            retrieved.Name.Should().Be(product.Name);
            retrieved.Quantity.Should().Be(product.Quantity);
            retrieved.Price.Should().Be(product.Price);
            retrieved.Description.Should().Be(product.Description);
            retrieved.Tags.Should().BeEquivalentTo(product.Tags);
        }

        [Fact]
        public async Task GetAsync_NonExistentKey_ReturnsNull()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            // Act
            var result = await provider.GetAsync("non-existent-key");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task SaveMany_MultipleEntities_AllSavedCorrectly()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            var products = Enumerable.Range(1, 10).Select(i => new Product
            {
                Id = $"product-{i:D3}",
                Name = $"Product {i}",
                Quantity = i * 10,
                Price = i * 9.99m
            }).ToList();

            var kvps = products.Select(p => new KeyValuePair<string, Product>(p.Id, p)).ToList();

            // Act
            await provider.SaveManyAsync(kvps);

            // Assert
            foreach (var product in products)
            {
                var retrieved = await provider.GetAsync(product.Id);
                retrieved.Should().NotBeNull();
                retrieved!.Id.Should().Be(product.Id);
                retrieved.Name.Should().Be(product.Name);
                retrieved.Quantity.Should().Be(product.Quantity);
                retrieved.Price.Should().Be(product.Price);
            }

            var count = await provider.CountAsync();
            count.Should().Be(10);
        }

        [Fact]
        public async Task GetMany_WithMixedKeys_ReturnsOnlyExistingEntities()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            var product1 = new Product { Id = "p1", Name = "Product 1" };
            var product2 = new Product { Id = "p2", Name = "Product 2" };
            
            await provider.SaveAsync(product1.Id, product1);
            await provider.SaveAsync(product2.Id, product2);

            // Act
            var keys = new[] { "p1", "p2", "p3", "p4" }; // p3 and p4 don't exist
            var results = await provider.GetManyAsync(keys);

            // Assert
            var resultList = results.ToList();
            resultList.Should().HaveCount(2);
            resultList.Should().Contain(p => p.Id == "p1");
            resultList.Should().Contain(p => p.Id == "p2");
        }

        [Fact]
        public async Task GetAll_WithPredicate_ReturnsFilteredResults()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            var products = new[]
            {
                new Product { Id = "p1", Name = "Expensive", Price = 150m, Quantity = 5 },
                new Product { Id = "p2", Name = "Cheap", Price = 50m, Quantity = 10 },
                new Product { Id = "p3", Name = "Medium", Price = 100m, Quantity = 7 },
                new Product { Id = "p4", Name = "Very Expensive", Price = 200m, Quantity = 3 }
            };

            foreach (var product in products)
            {
                await provider.SaveAsync(product.Id, product);
            }

            // Act
            var expensiveProducts = await provider.GetAllAsync(p => p.Price > 100m);

            // Assert
            expensiveProducts.Should().HaveCount(2);
            expensiveProducts.All(p => p.Price > 100m).Should().BeTrue();
            expensiveProducts.Should().Contain(p => p.Name == "Expensive");
            expensiveProducts.Should().Contain(p => p.Name == "Very Expensive");
        }

        [Fact]
        public async Task Delete_ExistingEntity_RemovesEntity()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            var product = new Product
            {
                Id = "delete-test",
                Name = "To Be Deleted",
                Price = 10m
            };

            await provider.SaveAsync(product.Id, product);
            var exists = await provider.ExistsAsync(product.Id);
            exists.Should().BeTrue();

            // Act
            await provider.DeleteAsync(product.Id);

            // Assert
            exists = await provider.ExistsAsync(product.Id);
            exists.Should().BeFalse();

            var retrieved = await provider.GetAsync(product.Id);
            retrieved.Should().BeNull();
        }

        [Fact]
        public async Task Delete_NonExistentEntity_DoesNotThrow()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            // Act & Assert - should not throw
            await provider.DeleteAsync("non-existent-key");
        }

        [Fact]
        public async Task Update_ExistingEntity_UpdatesCorrectly()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            var product = new Product
            {
                Id = "update-test",
                Name = "Original Name",
                Price = 100m,
                Quantity = 50
            };

            await provider.SaveAsync(product.Id, product);

            // Act
            product.Name = "Updated Name";
            product.Price = 150m;
            product.Quantity = 75;
            await provider.SaveAsync(product.Id, product);

            // Assert
            var retrieved = await provider.GetAsync(product.Id);
            retrieved.Should().NotBeNull();
            retrieved!.Name.Should().Be("Updated Name");
            retrieved.Price.Should().Be(150m);
            retrieved.Quantity.Should().Be(75);
        }

        [Fact]
        public async Task ConcurrentOperations_MultipleThreads_NoDataCorruption()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int threadCount = 10;
            const int operationsPerThread = 100;

            // Act
            var tasks = Enumerable.Range(0, threadCount).Select(threadId => Task.Run(async () =>
            {
                for (int i = 0; i < operationsPerThread; i++)
                {
                    var product = new Product
                    {
                        Id = $"thread-{threadId}-product-{i}",
                        Name = $"Thread {threadId} Product {i}",
                        Quantity = threadId * 100 + i,
                        Price = (threadId + 1) * (i + 1) * 1.99m
                    };

                    await provider.SaveAsync(product.Id, product);
                    
                    // Immediately read back
                    var retrieved = await provider.GetAsync(product.Id);
                    retrieved.Should().NotBeNull();
                    retrieved!.Id.Should().Be(product.Id);
                    retrieved.Name.Should().Be(product.Name);
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            // Assert
            var totalCount = await provider.CountAsync();
            totalCount.Should().Be(threadCount * operationsPerThread);

            // Verify a few random entities
            for (int t = 0; t < 3; t++)
            {
                for (int i = 0; i < 5; i++)
                {
                    var key = $"thread-{t}-product-{i}";
                    var product = await provider.GetAsync(key);
                    product.Should().NotBeNull();
                    product!.Name.Should().Be($"Thread {t} Product {i}");
                }
            }
        }

        [Fact]
        public async Task Clear_RemovesAllEntities()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            // Add some entities
            for (int i = 0; i < 20; i++)
            {
                await provider.SaveAsync($"clear-test-{i}", new Product
                {
                    Id = $"clear-test-{i}",
                    Name = $"Product {i}",
                    Price = i * 10m
                });
            }

            var beforeCount = await provider.CountAsync();
            beforeCount.Should().Be(20);

            // Act
            var deletedCount = await provider.ClearAsync();

            // Assert
            deletedCount.Should().Be(20);
            var afterCount = await provider.CountAsync();
            afterCount.Should().Be(0);

            // Verify individual entities are gone
            for (int i = 0; i < 20; i++)
            {
                var product = await provider.GetAsync($"clear-test-{i}");
                product.Should().BeNull();
            }
        }

        [Fact]
        public async Task Count_WithPredicate_ReturnsCorrectCount()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            var products = new[]
            {
                new Product { Id = "p1", Name = "Product 1", Price = 50m, Quantity = 100 },
                new Product { Id = "p2", Name = "Product 2", Price = 150m, Quantity = 50 },
                new Product { Id = "p3", Name = "Product 3", Price = 250m, Quantity = 25 },
                new Product { Id = "p4", Name = "Product 4", Price = 75m, Quantity = 75 },
                new Product { Id = "p5", Name = "Product 5", Price = 200m, Quantity = 10 }
            };

            foreach (var product in products)
            {
                await provider.SaveAsync(product.Id, product);
            }

            // Act
            var totalCount = await provider.CountAsync();
            var expensiveCount = await provider.CountAsync(p => p.Price > 100m);
            var lowStockCount = await provider.CountAsync(p => p.Quantity < 50);

            // Assert
            totalCount.Should().Be(5);
            expensiveCount.Should().Be(3); // p2, p3, p5
            lowStockCount.Should().Be(2); // p3, p5
        }

        [Fact]
        public async Task Exists_VariousScenarios_ReturnsCorrectResult()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            var product = new Product { Id = "exists-test", Name = "Test Product" };
            
            // Act & Assert - Before saving
            var existsBefore = await provider.ExistsAsync(product.Id);
            existsBefore.Should().BeFalse();

            // Save the product
            await provider.SaveAsync(product.Id, product);

            // Act & Assert - After saving
            var existsAfter = await provider.ExistsAsync(product.Id);
            existsAfter.Should().BeTrue();

            // Delete the product
            await provider.DeleteAsync(product.Id);

            // Act & Assert - After deletion
            var existsAfterDelete = await provider.ExistsAsync(product.Id);
            existsAfterDelete.Should().BeFalse();
        }

        [Fact]
        public async Task Provider_IsolatesBetweenDifferentEntityTypes()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var productProvider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create product provider");
            using var customerProvider = factory.Create<Customer>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create customer provider");

            var product = new Product { Id = "test-id", Name = "Test Product", Price = 100m };
            var customer = new Customer { Id = "test-id", Name = "Test Customer", Email = "test@example.com" };

            // Act - Save with same ID but different types
            await productProvider.SaveAsync(product.Id, product);
            await customerProvider.SaveAsync(customer.Id, customer);

            // Assert - Each provider maintains separate storage
            var retrievedProduct = await productProvider.GetAsync("test-id");
            var retrievedCustomer = await customerProvider.GetAsync("test-id");

            retrievedProduct.Should().NotBeNull();
            retrievedProduct!.Name.Should().Be("Test Product");

            retrievedCustomer.Should().NotBeNull();
            retrievedCustomer!.Name.Should().Be("Test Customer");

            // Count should be separate
            var productCount = await productProvider.CountAsync();
            var customerCount = await customerProvider.CountAsync();

            productCount.Should().Be(1);
            customerCount.Should().Be(1);
        }

        [Fact]
        public async Task LargeDataset_PerformsWell()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int entityCount = 10000;
            var products = Enumerable.Range(1, entityCount).Select(i => new Product
            {
                Id = $"perf-test-{i:D6}",
                Name = $"Performance Test Product {i}",
                Description = $"This is a description for product {i} with some additional text to make it larger",
                Price = (decimal)(i % 1000) + 0.99m,
                Quantity = i % 100,
                Tags = Enumerable.Range(0, i % 10).Select(t => $"tag-{t}").ToList()
            }).ToList();

            // Act - Measure insert time
            var insertStart = DateTime.UtcNow;
            foreach (var product in products)
            {
                await provider.SaveAsync(product.Id, product);
            }
            var insertDuration = DateTime.UtcNow - insertStart;

            // Act - Measure query time
            var queryStart = DateTime.UtcNow;
            var expensiveProducts = await provider.GetAllAsync(p => p.Price > 500m);
            var queryDuration = DateTime.UtcNow - queryStart;

            // Assert
            var count = await provider.CountAsync();
            count.Should().Be(entityCount);
            
            expensiveProducts.Count().Should().BeGreaterThan(0);
            
            // Performance assertions (these are generous to account for different environments)
            insertDuration.TotalSeconds.Should().BeLessThan(10, "Insert should complete within 10 seconds");
            queryDuration.TotalSeconds.Should().BeLessThan(1, "Query should complete within 1 second");

            this.output.WriteLine($"Inserted {entityCount} entities in {insertDuration.TotalMilliseconds}ms");
            this.output.WriteLine($"Queried {expensiveProducts.Count()} entities in {queryDuration.TotalMilliseconds}ms");
        }
    }
}