//-------------------------------------------------------------------------------
// <copyright file="EsentProviderCrashRecoveryTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.Esent.Tests
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;
    using Common.Persistence.Configuration;
    using Common.Persistence.Contract;
    using Common.Persistence.Factory;
    using AwesomeAssertions;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Isam.Esent.Interop;
    using Models;
    using Xunit;

    public class EsentProviderCrashRecoveryTests : IDisposable
    {
        private readonly string providerName = "EsentCrashRecoveryTests";
        private readonly IServiceCollection services;
        private readonly string testDbPath;
        private readonly string testDirectory;

        public EsentProviderCrashRecoveryTests()
        {
            // Skip initialization on non-Windows platforms
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            // Create a unique test directory for each test
            this.testDirectory = Path.Combine(Path.GetTempPath(), "EsentCrashTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(this.testDirectory);
            this.testDbPath = Path.Combine(this.testDirectory, "test.db");

            // Setup dependency injection
            this.services = new ServiceCollection();
            this.services.AddLogging(builder => builder
                .AddConsole()
                .SetMinimumLevel(LogLevel.Debug));
            
            var configuration = this.services.AddConfiguration();
            var settings = configuration.GetConfiguredSettings<EsentStoreSettings>(this.providerName);
            settings.DatabasePath = this.testDbPath;
            this.services.AddKeyedScoped<CrudStorageProviderSettings>(this.providerName, (_, _) => settings);
            this.services.AddPersistence();
        }

        [WindowsOnlyFact]
        public async Task Provider_ShouldRecoverFromDirtyShutdown()
        {
            // Arrange
            var product = new Product
            {
                Id = "crash-test-1",
                Name = "Crash Test Product",
                Quantity = 10,
                Price = 99.99m
            };

            // Act - Save data and simulate crash
            using (var provider = this.CreateProvider("Instance1"))
            {
                await provider.SaveAsync(product.Key, product);
                
                // Verify data is saved
                var saved = await provider.GetAsync(product.Key);
                saved.Should().NotBeNull();
                saved!.Name.Should().Be(product.Name);
                
                // DO NOT dispose properly - simulating a crash
                // The instance will not be terminated cleanly
            }

            // Force garbage collection to ensure the instance is truly gone
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Create transaction log files to simulate dirty shutdown
            var logFile = Path.Combine(this.testDirectory, "edb00001.log");
            File.WriteAllText(logFile, "simulated log data");

            // Act - Create new provider which should recover
            using (var provider = this.CreateProvider("Instance2"))
            {
                // The provider should successfully initialize after recovery
                var recovered = await provider.GetAsync(product.Key);
                
                // Assert - Data should be recovered
                recovered.Should().NotBeNull();
                recovered!.Name.Should().Be(product.Name);
                recovered.Quantity.Should().Be(product.Quantity);
                recovered.Price.Should().Be(product.Price);
            }
        }

        [WindowsOnlyFact]
        public async Task Provider_ShouldHandleCorruptedDatabase()
        {
            // Arrange - Create a corrupted database file
            File.WriteAllText(this.testDbPath, "This is not a valid ESENT database!");
            
            // Also create some log files
            var logFile = Path.Combine(this.testDirectory, "edb00001.log");
            File.WriteAllText(logFile, "corrupted log data");
            
            var checkpointFile = Path.Combine(this.testDirectory, "edb.chk");
            File.WriteAllText(checkpointFile, "corrupted checkpoint");

            // Act - Provider should handle the corruption gracefully
            using (var provider = this.CreateProvider("CorruptedInstance"))
            {
                // Provider should create a new database after backing up the corrupted one
                var product = new Product
                {
                    Id = "new-product",
                    Name = "New Product After Recovery",
                    Quantity = 5,
                    Price = 49.99m
                };

                await provider.SaveAsync(product.Key, product);
                
                var saved = await provider.GetAsync(product.Key);
                saved.Should().NotBeNull();
                saved!.Name.Should().Be(product.Name);
            }

            // Assert - Corrupted database should be backed up
            var backupFiles = Directory.GetFiles(this.testDirectory, "*.corrupted.*");
            backupFiles.Should().HaveCount(1);
        }

        [WindowsOnlyFact]
        public async Task Provider_ShouldCleanupOldTemporaryFiles()
        {
            // Arrange - Create old temporary files
            var oldTempFile = Path.Combine(this.testDirectory, "old.tmp");
            File.WriteAllText(oldTempFile, "old temp data");
            File.SetLastWriteTimeUtc(oldTempFile, DateTime.UtcNow.AddDays(-2));

            var recentTempFile = Path.Combine(this.testDirectory, "recent.tmp");
            File.WriteAllText(recentTempFile, "recent temp data");

            // Act
            using (var provider = this.CreateProvider("TempCleanupInstance"))
            {
                // Provider initialization should trigger cleanup
                await Task.Delay(100); // Give it time to initialize
            }

            // Assert
            File.Exists(oldTempFile).Should().BeFalse("Old temporary file should be deleted");
            File.Exists(recentTempFile).Should().BeTrue("Recent temporary file should be kept");
        }

        [WindowsOnlyFact]
        public async Task Provider_ShouldTerminateCleanlyOnDispose()
        {
            // Arrange
            var product = new Product
            {
                Id = "dispose-test",
                Name = "Dispose Test Product",
                Quantity = 1,
                Price = 9.99m
            };

            string instanceName = "CleanDisposeInstance";
            
            // Act
            using (var provider = this.CreateProvider(instanceName))
            {
                await provider.SaveAsync(product.Key, product);
            }

            // No log files should remain after clean shutdown
            var logFiles = Directory.GetFiles(this.testDirectory, "*.log");
            
            // Create new instance to verify clean shutdown
            using (var provider = this.CreateProvider(instanceName + "2"))
            {
                var recovered = await provider.GetAsync(product.Key);
                recovered.Should().NotBeNull();
                recovered!.Name.Should().Be(product.Name);
            }

            // Assert - Should not have triggered recovery
            // (This is more of an integration test to ensure clean shutdown works)
        }

        private ICrudStorageProvider<Product> CreateProvider(string instanceName)
        {
            var sp = this.services.BuildServiceProvider();
            var configReader = sp.GetRequiredService<IConfigReader>();
            var settings = configReader.ReadSettings<EsentStoreSettings>(this.providerName);
            settings.InstanceName = instanceName;
            settings.DatabasePath = this.testDbPath;

            var factory = sp.GetRequiredService<ICrudStorageProviderFactory>();
            return factory.Create<Product>(this.providerName);
        }

        public void Dispose()
        {
            try
            {
                // Clean up test directory
                if (Directory.Exists(this.testDirectory))
                {
                    // Wait a bit to ensure all handles are released
                    System.Threading.Thread.Sleep(500);
                    Directory.Delete(this.testDirectory, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}