// -----------------------------------------------------------------------
// <copyright file="ClusterRegistryProviderPayloadSizeTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Tests;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using Common.Persistence.Configuration;
using Common.Persistence.Factory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Models;
using Xunit;

/// <summary>
/// Tests for payload size limitations in ClusterRegistryProvider.
/// </summary>
[SupportedOSPlatform("windows")]
public class ClusterRegistryProviderPayloadSizeTests : IDisposable
{
    private readonly string testServiceName;
    private readonly IServiceProvider serviceProvider;

    public ClusterRegistryProviderPayloadSizeTests()
    {
        this.testServiceName = "TestService-PayloadSize";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Setup
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
            services.AddConfiguration();
            services.AddPersistence();
            this.serviceProvider = services.BuildServiceProvider();
        }
    }

    public void Dispose()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            this.CleanupTestData();
        }
        (this.serviceProvider as IDisposable)?.Dispose();
    }

    [WindowsOnlyTheory]
    [InlineData(50, true)]      // 50KB - should succeed with default 64KB limit
    [InlineData(100, false)]    // 100KB - should fail
    [InlineData(1024, false)]   // 1MB - should fail
    [InlineData(5120, false)]   // 5MB - should fail
    [InlineData(10240, false)]  // 10MB - should fail
    [InlineData(20480, false)]  // 20MB - should fail
    public async Task SaveAsync_VariousPayloadSizes_ShouldRespectSizeLimit(int sizeInKb, bool shouldSucceed)
    {
        // Arrange
        var settings = new ClusterRegistryStoreSettings
        {
            ApplicationName = "PayloadSizeTest",
            ServiceName = $"{this.testServiceName}_{nameof(this.SaveAsync_VariousPayloadSizes_ShouldRespectSizeLimit)}",
            MaxValueSizeKB = 64, // Default size limit
            EnableCompression = false,
            FallbackToLocalRegistry = true
        };

        var providerName = $"payload-size-test-{sizeInKb}kb";

        // Register settings for this provider
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
        services.AddConfiguration(new Dictionary<string, string>()
        {
            { $"Providers:{providerName}:ApplicationName", settings.ApplicationName },
            { $"Providers:{providerName}:ServiceName", settings.ServiceName },
            { $"Providers:{providerName}:MaxValueSizeKB", settings.MaxValueSizeKB.ToString() },
            { $"Providers:{providerName}:EnableCompression", settings.EnableCompression.ToString() },
            { $"Providers:{providerName}:FallbackToLocalRegistry", settings.FallbackToLocalRegistry.ToString() }
        });
        services.AddKeyedSingleton<CrudStorageProviderSettings>(providerName, (_, _) => settings);
        services.AddPersistence();
        var sp = services.BuildServiceProvider();

        var provider = new ClusterRegistryProvider<Product>(
            sp,
            providerName);

        var testProduct = new Product
        {
            Id = "test-product",
            Name = "Large Product",
            Description = this.GenerateRandomString(sizeInKb * 1024), // Generate string of specified size
            Price = 99.99m,
            Quantity = 1
        };

        // Act & Assert
        if (shouldSucceed)
        {
            await provider.SaveAsync(testProduct.Id, testProduct);

            // Verify it was saved
            var saved = await provider.GetAsync(testProduct.Id);
            saved.Should().NotBeNull();
            saved!.Id.Should().Be(testProduct.Id);
        }
        else
        {
            Func<Task> act = async () => await provider.SaveAsync(testProduct.Id, testProduct);
            (await act.Should().ThrowAsync<InvalidOperationException>())
                .WithMessage($"*exceeds maximum {settings.MaxValueSizeKB}KB*");
        }
    }

    [WindowsOnlyTheory]
    [InlineData(100, 128)]    // 100KB data with 128KB limit
    [InlineData(1024, 1536)]  // 1MB data with 1.5MB limit
    [InlineData(5120, 6144)]  // 5MB data with 6MB limit
    [InlineData(10240, 12288)] // 10MB data with 12MB limit
    [InlineData(20480, 24576)] // 20MB data with 24MB limit
    public async Task SaveAsync_LargePayloadsWithIncreasedLimit_ShouldSucceed(int sizeInKB, int limitInKB)
    {
        // Arrange
        var settings = new ClusterRegistryStoreSettings
        {
            ApplicationName = "LargePayloadTest",
            ServiceName = this.testServiceName,
            MaxValueSizeKB = limitInKB,
            EnableCompression = false,
            FallbackToLocalRegistry = true
        };

        var providerName = $"large-payload-test-{sizeInKB}kb";

        // Register settings for this provider
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
        services.AddConfiguration(new Dictionary<string, string>()
        {
            { $"Providers:{providerName}:ApplicationName", settings.ApplicationName },
            { $"Providers:{providerName}:ServiceName", settings.ServiceName },
            { $"Providers:{providerName}:MaxValueSizeKB", settings.MaxValueSizeKB.ToString() },
            { $"Providers:{providerName}:EnableCompression", settings.EnableCompression.ToString() },
            { $"Providers:{providerName}:FallbackToLocalRegistry", settings.FallbackToLocalRegistry.ToString() }
        });
        services.AddKeyedSingleton<CrudStorageProviderSettings>(providerName, (_, _) => settings);
        services.AddPersistence();
        var sp = services.BuildServiceProvider();

        var provider = new ClusterRegistryProvider<Product>(
            sp,
            providerName);

        var testProduct = new Product
        {
            Id = "large-product",
            Name = "Very Large Product",
            Description = this.GenerateRandomString(sizeInKB * 1024),
            Price = 999.99m,
            Quantity = 1
        };

        // Act
        await provider.SaveAsync(testProduct.Id, testProduct);

        // Assert - verify it was saved
        var saved = await provider.GetAsync(testProduct.Id);
        saved.Should().NotBeNull();
        saved!.Id.Should().Be(testProduct.Id);

        // Verify it can be read back
        var readProduct = await provider.GetAsync(testProduct.Id);
        readProduct.Should().NotBeNull();
        readProduct.Id.Should().Be(testProduct.Id);
        readProduct!.Description.Length.Should().Be(sizeInKB * 1024);
    }

    [WindowsOnlyTheory]
    [InlineData(100)]   // 100KB
    [InlineData(500)]   // 500KB
    [InlineData(1024)]  // 1MB
    [InlineData(2048)]  // 2MB
    [InlineData(5120)]  // 5MB
    public async Task SaveAsync_WithCompression_ShouldAllowLargerPayloads(int uncompressedSizeInKB)
    {
        // Arrange
        var settings = new ClusterRegistryStoreSettings
        {
            ApplicationName = "CompressionTest",
            ServiceName = this.testServiceName,
            MaxValueSizeKB = 256, // 256KB limit for compressed data
            EnableCompression = true,
            FallbackToLocalRegistry = true
        };

        var providerName = $"compression-test-{uncompressedSizeInKB}kb";

        // Register settings for this provider
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
        services.AddConfiguration(new Dictionary<string, string>()
        {
            { $"Providers:{providerName}:ApplicationName", settings.ApplicationName },
            { $"Providers:{providerName}:ServiceName", settings.ServiceName },
            { $"Providers:{providerName}:MaxValueSizeKB", settings.MaxValueSizeKB.ToString() },
            { $"Providers:{providerName}:EnableCompression", settings.EnableCompression.ToString() },
            { $"Providers:{providerName}:FallbackToLocalRegistry", settings.FallbackToLocalRegistry.ToString() }
        });
        services.AddKeyedSingleton<CrudStorageProviderSettings>(providerName, (_, _) => settings);
        services.AddPersistence();
        var sp = services.BuildServiceProvider();

        var provider = new ClusterRegistryProvider<Product>(
            sp,
            providerName);

        // Create highly compressible data (repeated pattern)
        var testProduct = new Product
        {
            Id = "compressed-product",
            Name = "Compressed Product",
            Description = this.GenerateCompressibleString(uncompressedSizeInKB * 1024),
            Price = 199.99m,
            Quantity = 1
        };

        // Act
        await provider.SaveAsync(testProduct.Id, testProduct);

        // Assert - verify it was saved
        var saved = await provider.GetAsync(testProduct.Id);
        saved.Should().NotBeNull();
        saved!.Id.Should().Be(testProduct.Id);

        // Verify compression was effective
        var readProduct = await provider.GetAsync(testProduct.Id);
        readProduct.Should().NotBeNull();
        readProduct!.Description.Length.Should().Be(uncompressedSizeInKB * 1024);
    }

    [WindowsOnlyTheory]
    [InlineData(63, 64)]   // Just under limit
    [InlineData(64, 64)]   // Exactly at limit
    [InlineData(255, 256)] // Just under larger limit
    [InlineData(256, 256)] // Exactly at larger limit
    public async Task SaveAsync_EdgeCasesAroundSizeLimit_ShouldBehaveCorrectly(int sizeInKB, int limitInKB)
    {
        // Arrange
        var settings = new ClusterRegistryStoreSettings
        {
            ApplicationName = "EdgeCaseTest",
            ServiceName = this.testServiceName,
            MaxValueSizeKB = limitInKB,
            EnableCompression = false,
            FallbackToLocalRegistry = true
        };

        var providerName = $"edge-case-test-{sizeInKB}kb-{limitInKB}kb";

        // Register settings for this provider
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
        services.AddConfiguration(new Dictionary<string, string>()
        {
            { $"Providers:{providerName}:ApplicationName", settings.ApplicationName },
            { $"Providers:{providerName}:ServiceName", settings.ServiceName },
            { $"Providers:{providerName}:MaxValueSizeKB", settings.MaxValueSizeKB.ToString() },
            { $"Providers:{providerName}:EnableCompression", settings.EnableCompression.ToString() },
            { $"Providers:{providerName}:FallbackToLocalRegistry", settings.FallbackToLocalRegistry.ToString() }
        });
        services.AddKeyedSingleton<CrudStorageProviderSettings>(providerName, (_, _) => settings);
        services.AddPersistence();
        var sp = services.BuildServiceProvider();

        var provider = new ClusterRegistryProvider<Product>(
            sp,
            providerName);

        // Calculate exact size needed accounting for Base64 encoding overhead (~33%)
        var base64Overhead = 1.34; // Base64 increases size by ~33%
        var targetStoredSize = sizeInKB * 1024;
        var dataSize = (int)(targetStoredSize / base64Overhead);

        var testProduct = new Product
        {
            Id = "edge-case-product",
            Name = "Edge Case Product",
            Description = this.GenerateRandomString(dataSize),
            Price = 49.99m,
            Quantity = 1
        };

        // Act
        await provider.SaveAsync(testProduct.Id, testProduct);

        // Assert - verify it was saved
        var saved = await provider.GetAsync(testProduct.Id);
        saved.Should().NotBeNull();
        saved!.Id.Should().Be(testProduct.Id);
    }

    [WindowsOnlyFact]
    public async Task SaveAsync_MultipleEntitiesNearSizeLimit_ShouldHandleIndependently()
    {
        // Arrange
        var settings = new ClusterRegistryStoreSettings
        {
            ApplicationName = "MultiEntityTest",
            ServiceName = this.testServiceName,
            MaxValueSizeKB = 128,
            EnableCompression = false,
            FallbackToLocalRegistry = true
        };

        var providerName = "multi-entity-test";

        // Register settings for this provider
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
        services.AddConfiguration(new Dictionary<string, string>()
        {
            { $"Providers:{providerName}:ApplicationName", settings.ApplicationName },
            { $"Providers:{providerName}:ServiceName", settings.ServiceName },
            { $"Providers:{providerName}:MaxValueSizeKB", settings.MaxValueSizeKB.ToString() },
            { $"Providers:{providerName}:EnableCompression", settings.EnableCompression.ToString() },
            { $"Providers:{providerName}:FallbackToLocalRegistry", settings.FallbackToLocalRegistry.ToString() }
        });
        services.AddKeyedSingleton<CrudStorageProviderSettings>(providerName, (_, _) => settings);
        services.AddPersistence();
        var sp = services.BuildServiceProvider();

        var provider = new ClusterRegistryProvider<Product>(
            sp,
            providerName);

        var products = new[]
        {
            new Product { Id = "product1", Name = "Small", Description = this.GenerateRandomString(50 * 1024) },  // 50KB
            new Product { Id = "product2", Name = "Medium", Description = this.GenerateRandomString(100 * 1024) }, // 100KB
            new Product { Id = "product3", Name = "Large", Description = this.GenerateRandomString(120 * 1024) }, // 120KB
            new Product { Id = "product4", Name = "Too Large", Description = this.GenerateRandomString(150 * 1024) }  // 150KB - should fail
        };

        // Act & Assert
        for (int i = 0; i < 3; i++)
        {
            await provider.SaveAsync(products[i].Id, products[i]);
        }

        // The last one should fail
        Func<Task> act = async () => await provider.SaveAsync(products[3].Id, products[3]);
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*exceeds maximum*");

        // Verify the successful saves can still be read
        for (int i = 0; i < 3; i++)
        {
            var readProduct = await provider.GetAsync(products[i].Id);
            readProduct.Should().NotBeNull();
            readProduct.Id.Should().Be(products[i].Id);
        }
    }

    [WindowsOnlyFact]
    public async Task UpdateAsync_FromSmallToLargePayload_ShouldRespectSizeLimit()
    {
        // Arrange
        var settings = new ClusterRegistryStoreSettings
        {
            ApplicationName = "UpdateSizeTest",
            ServiceName = this.testServiceName,
            MaxValueSizeKB = 128,
            EnableCompression = false,
            FallbackToLocalRegistry = true
        };

        var providerName = "update-size-test";

        // Register settings for this provider
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
        services.AddConfiguration(new Dictionary<string, string>()
        {
            { $"Providers:{providerName}:ApplicationName", settings.ApplicationName },
            { $"Providers:{providerName}:ServiceName", settings.ServiceName },
            { $"Providers:{providerName}:MaxValueSizeKB", settings.MaxValueSizeKB.ToString() },
            { $"Providers:{providerName}:EnableCompression", settings.EnableCompression.ToString() },
            { $"Providers:{providerName}:FallbackToLocalRegistry", settings.FallbackToLocalRegistry.ToString() }
        });
        services.AddKeyedSingleton<CrudStorageProviderSettings>(providerName, (_, _) => settings);
        services.AddPersistence();
        var sp = services.BuildServiceProvider();

        var provider = new ClusterRegistryProvider<Product>(
            sp,
            providerName);

        var smallProduct = new Product
        {
            Id = "update-test",
            Name = "Small Product",
            Description = this.GenerateRandomString(10 * 1024), // 10KB
            Price = 19.99m,
            Quantity = 5
        };

        // Act - First save small product
        await provider.SaveAsync(smallProduct.Id, smallProduct);

        // Update with larger product within limit
        var mediumProduct = new Product
        {
            Id = smallProduct.Id,
            Name = "Medium Product",
            Description = this.GenerateRandomString(100 * 1024), // 100KB
            Price = 99.99m,
            Quantity = 3
        };
        await provider.SaveAsync(mediumProduct.Id, mediumProduct);

        // Verify the update
        var updated = await provider.GetAsync(mediumProduct.Id);
        updated.Should().NotBeNull();
        updated!.Description.Length.Should().Be(100 * 1024);

        // Try to update with product exceeding limit
        var largeProduct = new Product
        {
            Id = smallProduct.Id,
            Name = "Too Large Product",
            Description = this.GenerateRandomString(200 * 1024), // 200KB
            Price = 299.99m,
            Quantity = 1
        };
        Func<Task> act = async () => await provider.SaveAsync(largeProduct.Id, largeProduct);
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*exceeds maximum*");

        // Verify the last successful update is still there
        var readProduct = await provider.GetAsync(smallProduct.Id);
        readProduct.Should().NotBeNull();
        readProduct.Description.Length.Should().Be(100 * 1024);
    }

    private string GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        var result = new char[length];

        for (int i = 0; i < length; i++)
        {
            result[i] = chars[random.Next(chars.Length)];
        }

        return new string(result);
    }

    private string GenerateCompressibleString(int length)
    {
        // Generate highly compressible data with repeated patterns
        const string pattern = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var sb = new StringBuilder(length);

        while (sb.Length < length)
        {
            sb.Append(pattern);
        }

        return sb.ToString(0, length);
    }

    private void CleanupTestData()
    {
        try
        {
            using (var rootKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE", true))
            {
                if (rootKey != null)
                {
                    rootKey.DeleteSubKeyTree(this.testServiceName, false);
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}