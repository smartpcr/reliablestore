//-------------------------------------------------------------------------------
// <copyright file="FileSystemProviderTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.FileSystem.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Common.Persistence.Configuration;
    using Common.Persistence.Contract;
    using Common.Persistence.Factory;
    using Common.Persistence.Providers.FileSystem;
    using AwesomeAssertions;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Models;
    using Xunit;
    using Xunit.Abstractions;

    public class FileSystemProviderTests : IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly IServiceProvider serviceProvider;
        private readonly string tempDirectory;
        private readonly string providerName = "FileSystemTest";

        public FileSystemProviderTests(ITestOutputHelper output)
        {
            this.output = output;

            // Create temp directory for tests
            this.tempDirectory = Path.Combine(Path.GetTempPath(), $"FSProviderTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(this.tempDirectory);

            // Setup DI container
            var services = new ServiceCollection();
            services.AddLogging(builder => builder
                .SetMinimumLevel(LogLevel.Debug));

            // Configure FileSystem provider
            var config = new Dictionary<string, string>
            {
                [$"Providers:{this.providerName}:Name"] = this.providerName,
                [$"Providers:{this.providerName}:FilePath"] = Path.Combine(this.tempDirectory, "entities", "dummy.json"),
                [$"Providers:{this.providerName}:UseSubdirectories"] = "true",
                [$"Providers:{this.providerName}:MaxRetries"] = "3",
                [$"Providers:{this.providerName}:RetryDelayMs"] = "50",
                [$"Providers:{this.providerName}:Enabled"] = "true"
            };

            var configuration = services.AddConfiguration(config);
            
            // Register settings
            var settings = configuration.GetConfiguredSettings<FileSystemStoreSettings>($"Providers:{this.providerName}");
            services.AddKeyedSingleton<CrudStorageProviderSettings>(this.providerName, (_, _) => settings);
            
            services.AddPersistence();

            this.serviceProvider = services.BuildServiceProvider();
        }

        public void Dispose()
        {
            // Cleanup temp directory
            if (Directory.Exists(this.tempDirectory))
            {
                try
                {
                    Directory.Delete(this.tempDirectory, true);
                }
                catch
                {
                    // Best effort
                }
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

            // Verify file exists
            var rootDir = Path.GetDirectoryName(Path.Combine(this.tempDirectory, "entities", "dummy.json"));
            var files = Directory.GetFiles(rootDir!, "*.json", SearchOption.AllDirectories);
            files.Should().HaveCount(1);
            this.output.WriteLine($"File created at: {files[0]}");
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
            }

            var count = await provider.CountAsync();
            count.Should().Be(10);
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
        public async Task ConcurrentOperations_MultipleThreads_NoDataCorruption()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            const int threadCount = 10;
            const int operationsPerThread = 50;

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

            // Verify files are deleted
            var rootDir = Path.GetDirectoryName(Path.Combine(this.tempDirectory, "entities", "dummy.json"));
            var files = Directory.GetFiles(rootDir!, "*.json", SearchOption.AllDirectories);
            files.Should().BeEmpty();
        }

        [Fact]
        public async Task Subdirectories_CreatedForKeys_ImprovePerformance()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName) 
                ?? throw new InvalidOperationException("Failed to create provider");

            var products = new[]
            {
                new Product { Id = "aa-product-1", Name = "AA Product 1" },
                new Product { Id = "ab-product-2", Name = "AB Product 2" },
                new Product { Id = "ba-product-3", Name = "BA Product 3" },
                new Product { Id = "bb-product-4", Name = "BB Product 4" }
            };

            // Act
            foreach (var product in products)
            {
                await provider.SaveAsync(product.Id, product);
            }

            // Assert
            var rootDir = Path.GetDirectoryName(Path.Combine(this.tempDirectory, "entities", "dummy.json"));
            
            // Check subdirectories were created
            var subDirs = Directory.GetDirectories(rootDir!);
            subDirs.Should().HaveCountGreaterThanOrEqualTo(2); // At least "aa", "ab", "ba", "bb"
            
            // Verify files are in correct subdirectories
            var aaFiles = Directory.GetFiles(Path.Combine(rootDir!, "aa"), "*.json");
            aaFiles.Should().HaveCount(1);
            
            var abFiles = Directory.GetFiles(Path.Combine(rootDir!, "ab"), "*.json");
            abFiles.Should().HaveCount(1);
            
            this.output.WriteLine($"Subdirectories created: {string.Join(", ", subDirs.Select(Path.GetFileName))}");
        }
    }
}