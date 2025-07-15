//-------------------------------------------------------------------------------
// <copyright file="ProviderBenchmarks.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Benchmarks
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;
    using BenchmarkDotNet.Attributes;
    using BenchmarkDotNet.Configs;
    using BenchmarkDotNet.Diagnosers;
    using BenchmarkDotNet.Engines;
    using BenchmarkDotNet.Jobs;
    using Common.Persistence.Configuration;
    using Common.Persistence.Contract;
    using Common.Persistence.Factory;
    using Common.Persistence.Providers.Esent;
    using Common.Persistence.Providers.ClusterRegistry;
    using Common.Persistence.Providers.FileSystem;
    using Common.Persistence.Providers.InMemory;
    using Common.Persistence.Providers.SqlServer;
    using Common.Persistence.Providers.SQLite;
    using DotNetEnv;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Models;

    [Config(typeof(BenchmarkConfig))]
    [SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 3, iterationCount: 5)]
    [MemoryDiagnoser]
    [ThreadingDiagnoser]
    public class ProviderBenchmarks
    {
        private IServiceProvider serviceProvider;
        private List<Product> testData;
        private ICrudStorageProvider<Product> provider;
        private string tempDirectory;

        public enum ProviderTypes
        {
            InMemory,
            FileSystem,
            Esent,
            ClusterRegistry,
            SqlServer,
            SQLite
        }

        public enum PayloadSizes
        {
            Small,
            Medium,
            Large
        }

        [Params(50)]
        public int OperationCount { get; set; }

        [Params(PayloadSizes.Small, PayloadSizes.Medium, PayloadSizes.Large)]
        public PayloadSizes PayloadSize { get; set; }

        [Params(ProviderTypes.InMemory, ProviderTypes.FileSystem, ProviderTypes.SQLite)]
        public ProviderTypes ProviderType { get; set; }

        [Params(8)]
        public int CoreCount { get; set; }

        [GlobalSetup]
        public void GlobalSetup()
        {
            Env.Load(); // Skip non-Windows providers on non-Windows platforms
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                (this.ProviderType == ProviderTypes.Esent || this.ProviderType == ProviderTypes.ClusterRegistry))
            {
                return;
            }

            // Setup temp directory
            this.tempDirectory = Path.Combine(@"C:\ClusterStorage\Infrastructure_1\Shares\SU1_Infrastructure_1\Updates\ReliableStore", $"Benchmark_{this.ProviderType}_{this.PayloadSize}");
            Directory.CreateDirectory(this.tempDirectory);

            // Setup DI container
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));

            // Add configuration
            var configuration = services.AddConfiguration(this.GetProviderConfiguration());
            var inMemorySettings = configuration.GetConfiguredSettings<InMemoryStoreSettings>("Providers:InMemory");
            services.AddKeyedSingleton<CrudStorageProviderSettings>("InMemory", (_, _) => inMemorySettings);
            var fileSystemSettings = configuration.GetConfiguredSettings<FileSystemStoreSettings>("Providers:FileSystem");
            services.AddKeyedSingleton<CrudStorageProviderSettings>("FileSystem", (_, _) => fileSystemSettings);
            var esentSettings = configuration.GetConfiguredSettings<EsentStoreSettings>("Providers:Esent");
            services.AddKeyedSingleton<CrudStorageProviderSettings>("Esent", (_, _) => esentSettings);
            var clusterRegistrySettings = configuration.GetConfiguredSettings<ClusterRegistryStoreSettings>($"Providers:ClusterRegistry");
            services.AddKeyedSingleton<CrudStorageProviderSettings>("ClusterRegistry", (_, _) => clusterRegistrySettings);
            var sqlServerSettings = configuration.GetConfiguredSettings<SqlServerProviderSettings>("Providers:SqlServer");
            services.AddKeyedSingleton<CrudStorageProviderSettings>("SqlServer", (_, _) => sqlServerSettings);
            var sqliteSettings = configuration.GetConfiguredSettings<SQLiteProviderSettings>("Providers:SQLite");
            services.AddKeyedSingleton<CrudStorageProviderSettings>("SQLite", (_, _) => sqliteSettings);

            // Register providers
            services.AddPersistence();

            this.serviceProvider = services.BuildServiceProvider();

            // Generate test data
            this.testData = this.GenerateTestData(this.OperationCount, this.PayloadSize);

            // Create provider
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            var providerName = this.ProviderType.ToString();
            this.provider = factory.Create<Product>(providerName);

            // Set CPU affinity
            this.SetCpuAffinity(this.CoreCount);
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            this.provider.Dispose();

            if (this.serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }

            // Cleanup temp directory
            if (Directory.Exists(this.tempDirectory))
            {
                try
                {
                    Directory.Delete(this.tempDirectory, true);
                }
                catch
                {
                    // ignored
                }
            }

            // Cleanup registry for ClusterRegistry provider
            if (this.ProviderType == ProviderTypes.ClusterRegistry && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(@"Software\BenchmarkTests", false);
                }
                catch
                {
                    // ignored
                }
            }

            // Cleanup SQL Server database
            if (this.ProviderType == ProviderTypes.SqlServer)
            {
                try
                {
                    using var connection = new Microsoft.Data.SqlClient.SqlConnection("Server=localhost;Database=master;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=true");
                    connection.Open();
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = "IF DB_ID('ReliableStoreBenchmark') IS NOT NULL DROP DATABASE [ReliableStoreBenchmark]";
                    cmd.ExecuteNonQuery();
                }
                catch
                {
                    // ignored
                }
            }
        }

        [Benchmark(Description = "Sequential Write Operations")]
        public async Task SequentialWrites()
        {
            foreach (var product in this.testData)
            {
                await this.provider.SaveAsync(product.Id, product);
            }
        }

        [Benchmark(Description = "Sequential Read Operations")]
        public async Task SequentialReads()
        {
            // First write all data
            foreach (var product in this.testData)
            {
                await this.provider.SaveAsync(product.Id, product);
            }

            // Then read all data
            foreach (var product in this.testData)
            {
                await this.provider.GetAsync(product.Id);
            }
        }

        [Benchmark(Description = "Mixed Operations (70% Read, 20% Write, 10% Delete)")]
        public async Task MixedOperations()
        {
            // First write initial data (50% of total)
            var initialCount = this.testData.Count / 2;
            for (var i = 0; i < initialCount; i++)
            {
                await this.provider.SaveAsync(this.testData[i].Id, this.testData[i]);
            }

            // Perform mixed operations
            var random = new Random(42);
            for (var i = 0; i < this.testData.Count; i++)
            {
                var operation = random.Next(100);

                if (operation < 70) // 70% reads
                {
                    var index = random.Next(initialCount);
                    await this.provider.GetAsync(this.testData[index].Id);
                }
                else if (operation < 90) // 20% writes
                {
                    var index = initialCount + (i % (this.testData.Count - initialCount));
                    await this.provider.SaveAsync(this.testData[index].Id, this.testData[index]);
                }
                else // 10% deletes
                {
                    var index = random.Next(initialCount);
                    await this.provider.DeleteAsync(this.testData[index].Id);
                }
            }
        }

        [Benchmark(Description = "Batch Operations")]
        public async Task BatchOperations()
        {
            const int batchSize = 100;

            // Process in batches
            for (var i = 0; i < this.testData.Count; i += batchSize)
            {
                var batch = this.testData.Skip(i).Take(batchSize).ToList();
                var tasks = batch.Select(p => this.provider.SaveAsync(p.Id, p)).ToArray();
                await Task.WhenAll(tasks);
            }
        }

        [Benchmark(Description = "GetAll Operation")]
        public async Task GetAllOperation()
        {
            // First write all data
            foreach (var product in this.testData)
            {
                await this.provider.SaveAsync(product.Id, product);
            }

            // Then get all
            await this.provider.GetAllAsync();
        }

        private Dictionary<string, string> GetProviderConfiguration()
        {
            var config = new Dictionary<string, string>();

            // InMemory provider config
            config["Providers:InMemory:Name"] = "InMemory";
            config["Providers:InMemory:Enabled"] = "true";

            // FileSystem provider config
            config["Providers:FileSystem:Name"] = "FileSystem";
            config["Providers:FileSystem:FolderPath"] = Path.Combine(this.tempDirectory, "FileSystem");
            config["Providers:FileSystem:Enabled"] = "true";

            // ESENT provider config
            config["Providers:Esent:Name"] = "Esent";
            config["Providers:Esent:DatabasePath"] = Path.Combine(this.tempDirectory, "Esent", "benchmark.db");
            config["Providers:Esent:InstanceName"] = "BenchmarkInstance";
            config["Providers:Esent:UseSessionPool"] = "true";
            config["Providers:Esent:Enabled"] = "true";

            // ClusterRegistry provider config
            config["Providers:ClusterRegistry:Name"] = "ClusterRegistry";
            config["Providers:ClusterRegistry:ClusterName"] = "s-Cluster";
            config["Providers:ClusterRegistry:RootPath"] = @"Software\BenchmarkTests";
            config["Providers:ClusterRegistry:ApplicationName"] = "ReliableStoreBenchmark";
            config["Providers:ClusterRegistry:ServiceName"] = $"Benchmark_{Guid.NewGuid():N}";
            config["Providers:ClusterRegistry:FallbackToLocalRegistry"] = "true";
            config["Providers:ClusterRegistry:EnableCompression"] = "true";
            config["Providers:ClusterRegistry:MaxValueSizeKB"] = "46080"; // 45 MB
            config["Providers:ClusterRegistry:ConnectionTimeoutSeconds"] = "30";
            config["Providers:ClusterRegistry:RetryCount"] = "3";
            config["Providers:ClusterRegistry:RetryDelayMilliseconds"] = "100";
            config["Providers:ClusterRegistry:Enabled"] = "true";

            // SQL Server provider config
            config["Providers:SqlServer:Name"] = "SqlServer";
            config["Providers:SqlServer:Schema"] = "benchmark";
            config["Providers:SqlServer:CommandTimeout"] = "300";
            config["Providers:SqlServer:CreateTableIfNotExists"] = "true";
            config["Providers:SqlServer:Enabled"] = "true";
            config["Providers:SqlServer:AssemblyName"] = "CRP.Common.Persistence.Providers.SqlServer";
            config["Providers:SqlServer:TypeName"] = "Common.Persistence.Providers.SqlServer.SqlServerProvider`1";
            config["Providers:SqlServer:Capabilities"] = "1";
            config["Providers:SqlServer:Host"] = Environment.GetEnvironmentVariable("DB_HOST")!;
            config["Providers:SqlServer:Port"] = Environment.GetEnvironmentVariable("DB_PORT")!;
            config["Providers:SqlServer:DbName"] = Environment.GetEnvironmentVariable("DB_NAME")!;
            config["Providers:SqlServer:UserId"] = Environment.GetEnvironmentVariable("DB_USER")!;
            config["Providers:SqlServer:Password"] = Environment.GetEnvironmentVariable("DB_PASSWORD")!;

            // SQLite provider config
            config["Providers:SQLite:Name"] = "SQLite";
            config["Providers:SQLite:DataSource"] = Path.Combine(this.tempDirectory, "SQLite", "benchmark.db");
            config["Providers:SQLite:Schema"] = "benchmark";
            config["Providers:SQLite:Mode"] = "ReadWriteCreate";
            config["Providers:SQLite:Cache"] = "Shared";
            config["Providers:SQLite:ForeignKeys"] = "true";
            config["Providers:SQLite:CommandTimeout"] = "300";
            config["Providers:SQLite:CreateTableIfNotExists"] = "true";
            config["Providers:SQLite:Enabled"] = "true";
            config["Providers:SQLite:AssemblyName"] = "CRP.Common.Persistence.Providers.SQLite";
            config["Providers:SQLite:TypeName"] = "Common.Persistence.Providers.SQLite.SQLiteProvider`1";
            config["Providers:SQLite:Capabilities"] = "1";

            // Performance tuning options for SQLite
            config["Providers:SQLite:JournalMode"] = "WAL";           // Write-Ahead Logging for better concurrency
            config["Providers:SQLite:SynchronousMode"] = "Normal";    // Good balance of safety and speed
            config["Providers:SQLite:CacheSize"] = "-10000";          // 10MB cache for better performance
            config["Providers:SQLite:PageSize"] = "8192";             // 8KB pages for better I/O performance

            return config;
        }

        private List<Product> GenerateTestData(int count, PayloadSizes payloadSize)
        {
            var products = new List<Product>(count);
            var descriptionSize = payloadSize switch
            {
                PayloadSizes.Small => 1000,      // ~1 KB
                PayloadSizes.Medium => 100_000,    // ~100 KB
                PayloadSizes.Large => 5_000_000, // ~5 MB
                _ => 1000
            };

            for (var i = 0; i < count; i++)
            {
                products.Add(new Product
                {
                    Id = $"product-{i:D8}",
                    Name = $"Product {i}",
                    Description = new string('x', descriptionSize),
                    Quantity = i % 1000,
                    Price = (i % 100) * 9.99m,
                    Tags = Enumerable.Range(0, 10).Select(j => $"tag{j}").ToList()
                });
            }

            return products;
        }

        private void SetCpuAffinity(int coreCount)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Set process affinity mask to use only specified number of cores
                var affinity = (1 << coreCount) - 1; // Create bitmask for first N cores
                var process = System.Diagnostics.Process.GetCurrentProcess();
                process.ProcessorAffinity = new IntPtr(affinity);
            }
            // Note: On Linux, you would use taskset or similar approach
        }

        public class BenchmarkConfig : ManualConfig
        {
            public BenchmarkConfig()
            {
                this.AddDiagnoser(MemoryDiagnoser.Default);
                this.AddDiagnoser(ThreadingDiagnoser.Default);

                // Use default toolchain (out-of-process) for long-running benchmarks
                // This avoids timeout issues with ESENT provider
                this.AddJob(Job.Default
                    .WithStrategy(RunStrategy.Throughput));
            }
        }
    }
}