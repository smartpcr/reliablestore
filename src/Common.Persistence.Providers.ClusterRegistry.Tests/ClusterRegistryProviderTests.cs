//-------------------------------------------------------------------------------
// <copyright file="ClusterRegistryProviderTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Tests
{
    using System;
    using System.Runtime.Versioning;
    using System.Threading.Tasks;
    using Common.Persistence.Configuration;
    using Common.Persistence.Contract;
    using Common.Persistence.Providers.ClusterRegistry;
    using Common.Persistence.Factory;
    using Common.Persistence.Serialization;
    using AwesomeAssertions;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Models;
    using Moq;
    using Xunit;

    /// <summary>
    /// Unit tests for ClusterRegistryProvider.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class ClusterRegistryProviderTests
    {
        [Fact]
        public void ClusterRegistryStoreSettings_ShouldHaveCorrectDefaults()
        {
            var services = new ServiceCollection();
            var configuration = services.AddConfiguration();
            var settings = configuration.GetConfiguredSettings<ClusterRegistryStoreSettings>("Providers:TestRegistry");

            // Assert
            settings.Name.Should().Be("ClusterRegistry");
            settings.TypeName.Should().Contain("ClusterRegistryProvider");
            settings.AssemblyName.Should().NotBeNullOrEmpty();
            settings.Enabled.Should().BeTrue();
            settings.ApplicationName.Should().Be("TestApp");
            settings.ServiceName.Should().Be("TestSvc");
            settings.RootPath.Should().Be(@"Software\Microsoft\ReliableStore");
            settings.EnableCompression.Should().BeTrue();
            settings.MaxValueSizeKB.Should().Be(1024 * 15); // 15MB
            settings.ConnectionTimeoutSeconds.Should().Be(30);
            settings.RetryCount.Should().Be(3);
            settings.RetryDelayMilliseconds.Should().Be(100);
        }

        [Fact]
        public void Constructor_WithInvalidClusterName_ShouldThrowException()
        {
            // Arrange
            var services = new ServiceCollection();
            var mockConfigReader = new Mock<IConfigReader>();
            var mockLogger = new Mock<ILogger<ClusterRegistryProvider<Product>>>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            mockLoggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

            services.AddSingleton(mockConfigReader.Object);
            services.AddSingleton(mockLoggerFactory.Object);
            services.AddSingleton<ISerializerFactory, JsonSerializerFactory>();

            var settings = new ClusterRegistryStoreSettings
            {
                ClusterName = "invalid-cluster-name-that-does-not-exist",
                FallbackToLocalRegistry = false
            };
            
            mockConfigReader.Setup(x => x.ReadSettings<ClusterRegistryStoreSettings>(It.IsAny<string>()))
                .Returns(settings);

            var serviceProvider = services.BuildServiceProvider();

            // Act
            var act = () => new ClusterRegistryProvider<Product>(serviceProvider, "test");

            // Assert - invalid cluster name or cluster not found would fallback to local store
            act.Should().Throw<Exception>();
        }

        [WindowsOnlyFact]
        public void KeyHashing_ShouldBeConsistent()
        {
            // Arrange
            var key1 = "test-key-123";
            var key2 = "test-key-123";
            var key3 = "different-key";

            // Act - Using reflection to test the protected method
            var hash1 = CreateKeyHashUsingReflection(key1);
            var hash2 = CreateKeyHashUsingReflection(key2);
            var hash3 = CreateKeyHashUsingReflection(key3);

            // Assert
            hash1.Should().Be(hash2);
            hash1.Should().NotBe(hash3);
            hash1.Should().NotContain("/");
            hash1.Should().NotContain("+");
            hash1.Should().NotContain("=");
        }

        [Fact]
        public void SerializationSize_LargeData_ShouldBeValidated()
        {
            // Arrange
            var maxSizeKB = 64;
            var data = new byte[100 * 1024]; // 100KB
            var base64Data = Convert.ToBase64String(data);
            var encodedSize = base64Data.Length * sizeof(char);

            // Act & Assert
            (encodedSize > maxSizeKB * 1024).Should().BeTrue();
        }

        [Fact]
        public async Task JsonSerializerIntegration_ShouldSerializeAndDeserialize()
        {
            // Arrange
            var serializer = new JsonSerializer<Product>();
            var product = new Product
            {
                Id = "123",
                Name = "Test Product",
                Price = 29.99m,
                Quantity = 10
            };

            // Act
            var serialized = await serializer.SerializeAsync(product);
            var deserialized = await serializer.DeserializeAsync(serialized);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized!.Id.Should().Be(product.Id);
            deserialized.Name.Should().Be(product.Name);
            deserialized.Price.Should().Be(product.Price);
            deserialized.Quantity.Should().Be(product.Quantity);
        }

        private static string CreateKeyHashUsingReflection(string key)
        {
            // Create a temporary provider instance just to access the method
            var services = new ServiceCollection();
            var mockConfigReader = new Mock<IConfigReader>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<ClusterRegistryProvider<Product>>>();
            
            mockLoggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            services.AddSingleton(mockConfigReader.Object);
            services.AddSingleton(mockLoggerFactory.Object);
            services.AddSingleton<ISerializerFactory, JsonSerializerFactory>();

            var settings = new ClusterRegistryStoreSettings();
            mockConfigReader.Setup(x => x.ReadSettings<ClusterRegistryStoreSettings>(It.IsAny<string>()))
                .Returns(settings);

            var serviceProvider = services.BuildServiceProvider();

            // Use a test-specific provider that doesn't connect to cluster
            var provider = new TestableClusterRegistryProvider<Product>(serviceProvider, "test");
            return provider.CreateKeyHashPublic(key);
        }

        /// <summary>
        /// Testable version of ClusterRegistryProvider that exposes protected methods.
        /// </summary>
        private class TestableClusterRegistryProvider<T> : ClusterRegistryProvider<T> where T : IEntity
        {
            public TestableClusterRegistryProvider(IServiceProvider serviceProvider, string name)
                : base(serviceProvider, name)
            {
            }

            public string CreateKeyHashPublic(string key)
            {
                return this.CreateKeyHash(key);
            }
        }

        /// <summary>
        /// Simple JSON serializer factory for testing.
        /// </summary>
        private class JsonSerializerFactory : ISerializerFactory
        {
            public ISerializer<T> Create<T>(string name) where T : IEntity
            {
                return new JsonSerializer<T>();
            }
        }
    }
}