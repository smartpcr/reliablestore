//-------------------------------------------------------------------------------
// <copyright file="SqlServerProviderPerformanceTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.SqlServer.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading.Tasks;
    using AwesomeAssertions;
    using Common.Persistence.Configuration;
    using Common.Persistence.Factory;
    using DotNetEnv;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Models;
    using Xunit;
    using Xunit.Abstractions;

    public class SqlServerProviderPerformanceTests : IAsyncLifetime, IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly string providerName = "SqlServerPerfTest";
        private readonly string schemaName = "test3";
        private IServiceProvider serviceProvider;
        private ICrudStorageProviderFactory? factory;

        public SqlServerProviderPerformanceTests(ITestOutputHelper output)
        {
            this.output = output;
            Env.Load();
        }

        public Task InitializeAsync()
        {
            // Setup DI container
            var services = new ServiceCollection();
            services.AddLogging(builder => builder
                .SetMinimumLevel(LogLevel.Debug)
                .AddXunit(this.output));

            // Configure SQL Server provider
            var config = new Dictionary<string, string>
            {
                [$"Providers:{this.providerName}:Name"] = this.providerName,
                [$"Providers:{this.providerName}:AssemblyName"] = "CRP.Common.Persistence.Providers.SqlServer",
                [$"Providers:{this.providerName}:TypeName"] = "Common.Persistence.Providers.SqlServer.SqlServerProvider`1",
                [$"Providers:{this.providerName}:Enabled"] = "true",
                [$"Providers:{this.providerName}:Capabilities"] = "1",
                [$"Providers:{this.providerName}:Host"] = Environment.GetEnvironmentVariable("DB_HOST")!,
                [$"Providers:{this.providerName}:Port"] = Environment.GetEnvironmentVariable("DB_PORT")!,
                [$"Providers:{this.providerName}:DbName"] = Environment.GetEnvironmentVariable("DB_NAME")!,
                [$"Providers:{this.providerName}:UserId"] = Environment.GetEnvironmentVariable("DB_USER")!,
                [$"Providers:{this.providerName}:Password"] = Environment.GetEnvironmentVariable("DB_PASSWORD")!,
                [$"Providers:{this.providerName}:CommandTimeout"] = "60",
                [$"Providers:{this.providerName}:EnableRetryLogic"] = "true",
                [$"Providers:{this.providerName}:MaxRetryCount"] = "3",
                [$"Providers:{this.providerName}:CreateTableIfNotExists"] = "true",
                [$"Providers:{this.providerName}:Schema"] = this.schemaName
            };

            var configuration = services.AddConfiguration(config);

            // Register settings
            var settings = configuration.GetConfiguredSettings<SqlServerProviderSettings>($"Providers:{this.providerName}");
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
        public async Task BulkWrite_Performance_MeetsExpectations()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            const int entityCount = 1000;
            var products = Enumerable.Range(1, entityCount).Select(i => new Product
            {
                Id = $"perf-product-{i:D6}",
                Name = $"Performance Test Product {i}",
                Quantity = i * 10,
                Price = i * 9.99m,
                Description = $"This is a performance test product with id {i}",
                Tags = new List<string> { "perf", "test", $"batch-{i / 100}" }
            }).ToList();

            // Act
            var stopwatch = Stopwatch.StartNew();

            var tasks = products.Select(p => provider.SaveAsync(p.Key, p));
            await Task.WhenAll(tasks);

            stopwatch.Stop();

            // Assert
            this.output.WriteLine($"Bulk write of {entityCount} entities took {stopwatch.ElapsedMilliseconds}ms");
            this.output.WriteLine($"Average write time: {stopwatch.ElapsedMilliseconds / (double)entityCount:F2}ms per entity");

            // SQL Server should handle 1000 entities in under 30 seconds
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(30000);
        }

        [Fact]
        public async Task BulkRead_Performance_MeetsExpectations()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            const int entityCount = 500;
            var products = Enumerable.Range(1, entityCount).Select(i => new Product
            {
                Id = $"read-perf-product-{i:D6}",
                Name = $"Read Performance Test Product {i}",
                Quantity = i * 10,
                Price = i * 9.99m
            }).ToList();

            // Setup - save all products first
            foreach (var product in products)
            {
                await provider.SaveAsync(product.Key, product);
            }

            var keys = products.Select(p => p.Key).ToList();

            // Act
            var stopwatch = Stopwatch.StartNew();

            var retrieved = await provider.GetManyAsync(keys);

            stopwatch.Stop();

            // Assert
            var retrievedList = retrieved.ToList();
            retrievedList.Should().HaveCount(entityCount);

            this.output.WriteLine($"Bulk read of {entityCount} entities took {stopwatch.ElapsedMilliseconds}ms");
            this.output.WriteLine($"Average read time: {stopwatch.ElapsedMilliseconds / (double)entityCount:F2}ms per entity");

            // SQL Server should read 500 entities in under 5 seconds
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000);
        }

        [Fact]
        public async Task MixedOperations_Performance_HandlesLoadWell()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            const int operationCount = 100;
            var random = new Random(42);
            var existingProducts = new List<Product>();

            // Setup some initial data
            for (int i = 0; i < 50; i++)
            {
                var product = new Product
                {
                    Id = $"mixed-product-{i:D6}",
                    Name = $"Mixed Test Product {i}",
                    Quantity = i * 10
                };
                await provider.SaveAsync(product.Key, product);
                existingProducts.Add(product);
            }

            // Act
            var stopwatch = Stopwatch.StartNew();
            var tasks = new List<Task>();

            for (int i = 0; i < operationCount; i++)
            {
                var operation = random.Next(4);

                switch (operation)
                {
                    case 0: // Create
                        var newProduct = new Product
                        {
                            Id = $"mixed-new-product-{i:D6}",
                            Name = $"New Mixed Product {i}",
                            Quantity = i * 5
                        };
                        tasks.Add(provider.SaveAsync(newProduct.Key, newProduct));
                        break;

                    case 1: // Read
                        if (existingProducts.Any())
                        {
                            var readProduct = existingProducts[random.Next(existingProducts.Count)];
                            tasks.Add(provider.GetAsync(readProduct.Key));
                        }
                        break;

                    case 2: // Update
                        if (existingProducts.Any())
                        {
                            var updateProduct = existingProducts[random.Next(existingProducts.Count)];
                            updateProduct.Quantity += 10;
                            updateProduct.Version++;
                            tasks.Add(provider.SaveAsync(updateProduct.Key, updateProduct));
                        }
                        break;

                    case 3: // Check exists
                        if (existingProducts.Any())
                        {
                            var checkProduct = existingProducts[random.Next(existingProducts.Count)];
                            tasks.Add(provider.ExistsAsync(checkProduct.Key));
                        }
                        break;
                }
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            this.output.WriteLine($"Mixed operations ({operationCount} total) took {stopwatch.ElapsedMilliseconds}ms");
            this.output.WriteLine($"Average operation time: {stopwatch.ElapsedMilliseconds / (double)operationCount:F2}ms");

            // Mixed operations should complete in reasonable time
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(10000);
        }

        [Fact]
        public async Task GetAll_LargeDataset_PerformsAcceptably()
        {
            // Arrange
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            using var provider = factory.Create<Product>(this.providerName)
                ?? throw new InvalidOperationException("Failed to create provider");

            // Clear any existing data
            await provider.ClearAsync();

            const int entityCount = 200;
            var products = Enumerable.Range(1, entityCount).Select(i => new Product
            {
                Id = $"getall-perf-product-{i:D6}",
                Name = $"GetAll Performance Product {i}",
                Quantity = i * 10,
                Price = i % 2 == 0 ? 50m : 150m
            }).ToList();

            // Setup
            foreach (var product in products)
            {
                await provider.SaveAsync(product.Key, product);
            }

            // Act
            var stopwatch = Stopwatch.StartNew();

            var allProducts = await provider.GetAllAsync();
            var expensiveProducts = await provider.GetAllAsync(p => p.Price > 100m);

            stopwatch.Stop();

            // Assert
            allProducts.Count().Should().Be(entityCount);
            expensiveProducts.Count().Should().Be(entityCount / 2);

            this.output.WriteLine($"GetAll operations for {entityCount} entities took {stopwatch.ElapsedMilliseconds}ms");

            // GetAll should complete in reasonable time
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000);
        }
    }
}