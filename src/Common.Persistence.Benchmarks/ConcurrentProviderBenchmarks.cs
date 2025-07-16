//-------------------------------------------------------------------------------
// <copyright file="ConcurrentProviderBenchmarks.cs" company="Microsoft Corp.">
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
    using Common.Persistence.Providers.SQLite;
    using DotNetEnv;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Models;

    [Config(typeof(BenchmarkConfig))]
    [SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 2, iterationCount: 3)]
    [MemoryDiagnoser]
    [ThreadingDiagnoser]
    public class ConcurrentProviderBenchmarks
    {
        private IServiceProvider serviceProvider;
        private List<Product> testData;
        private ICrudStorageProvider<Product> provider;
        private string tempDirectory;

        [Params(100)]
        public int OperationCount { get; set; }

        [Params("Small", "Medium", "Large")]
        public string PayloadSize { get; set; }

        [Params("InMemory", "FileSystem", "SQLite")]
        public string ProviderType { get; set; }

        [Params(4)]
        public int ThreadCount { get; set; }

        [GlobalSetup]
        public void GlobalSetup()
        {
            Env.Load();

            // Skip non-Windows providers on non-Windows platforms
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                (this.ProviderType == "Esent" || this.ProviderType == "ClusterRegistry"))
            {
                return;
            }

            // Setup temp directory
            this.tempDirectory = Path.Combine(@"C:\ClusterStorage\Infrastructure_1\Shares\SU1_Infrastructure_1\Updates\ReliableStore", $"BenchmarkConcurrent_{this.ProviderType}_{this.PayloadSize}");
            Directory.CreateDirectory(this.tempDirectory);

            // Setup DI container
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

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
            var sqliteSettings = configuration.GetConfiguredSettings<SQLiteProviderSettings>("Providers:SQLite");
            services.AddKeyedSingleton<CrudStorageProviderSettings>("SQLite", (_, _) => sqliteSettings);

            // Register providers
            services.AddPersistence();

            this.serviceProvider = services.BuildServiceProvider();

            // Generate test data
            this.testData = this.GenerateTestData(this.OperationCount, this.PayloadSize);

            // Create provider
            var factory = this.serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            this.provider = factory.Create<Product>(this.ProviderType);
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
            if (this.ProviderType == "ClusterRegistry" && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
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
        }

        [Benchmark(Description = "Concurrent Write Operations")]
        public async Task ConcurrentWrites()
        {
            var itemsPerThread = this.OperationCount / this.ThreadCount;
            var tasks = new Task[this.ThreadCount];

            for (var t = 0; t < this.ThreadCount; t++)
            {
                var threadId = t;
                tasks[t] = Task.Run(async () =>
                {
                    var start = threadId * itemsPerThread;
                    var end = (threadId == this.ThreadCount - 1) ? this.OperationCount : start + itemsPerThread;

                    for (var i = start; i < end; i++)
                    {
                        await this.provider.SaveAsync(this.testData[i].Id, this.testData[i]);
                    }
                });
            }

            await Task.WhenAll(tasks);
        }

        [Benchmark(Description = "Concurrent Read Operations")]
        public async Task ConcurrentReads()
        {
            // First write all data sequentially
            foreach (var product in this.testData)
            {
                await this.provider.SaveAsync(product.Id, product);
            }

            // Then read concurrently
            var itemsPerThread = this.OperationCount / this.ThreadCount;
            var tasks = new Task[this.ThreadCount];

            for (var t = 0; t < this.ThreadCount; t++)
            {
                var threadId = t;
                tasks[t] = Task.Run(async () =>
                {
                    var start = threadId * itemsPerThread;
                    var end = (threadId == this.ThreadCount - 1) ? this.OperationCount : start + itemsPerThread;

                    for (var i = start; i < end; i++)
                    {
                        await this.provider.GetAsync(this.testData[i].Id);
                    }
                });
            }

            await Task.WhenAll(tasks);
        }

        [Benchmark(Description = "Concurrent Mixed Operations")]
        public async Task ConcurrentMixedOperations()
        {
            // First write initial data (50% of total)
            var initialCount = this.testData.Count / 2;
            for (var i = 0; i < initialCount; i++)
            {
                await this.provider.SaveAsync(this.testData[i].Id, this.testData[i]);
            }

            // Perform concurrent mixed operations
            var operationsPerThread = this.OperationCount / this.ThreadCount;
            var tasks = new Task[this.ThreadCount];

            for (var t = 0; t < this.ThreadCount; t++)
            {
                var threadId = t;
                tasks[t] = Task.Run(async () =>
                {
                    var random = new Random(42 + threadId);

                    for (var i = 0; i < operationsPerThread; i++)
                    {
                        var operation = random.Next(100);

                        if (operation < 70) // 70% reads
                        {
                            var index = random.Next(initialCount);
                            await this.provider.GetAsync(this.testData[index].Id);
                        }
                        else if (operation < 90) // 20% writes
                        {
                            var index = initialCount + ((threadId * operationsPerThread + i) % (this.testData.Count - initialCount));
                            await this.provider.SaveAsync(this.testData[index].Id, this.testData[index]);
                        }
                        else // 10% deletes
                        {
                            var index = random.Next(initialCount);
                            try
                            {
                                await this.provider.DeleteAsync(this.testData[index].Id);
                            }
                            catch
                            {
                                // Ignore if already deleted by another thread
                            }
                        }
                    }
                });
            }

            await Task.WhenAll(tasks);
        }

        private Dictionary<string, string> GetProviderConfiguration()
        {
            var config = new Dictionary<string, string>();

            // InMemory provider config
            config["Providers:InMemory:Name"] = "InMemory";
            config["Providers:InMemory:Enabled"] = "true";

            // FileSystem provider config
            config["Providers:FileSystem:Name"] = "FileSystem";
            config["Providers:FileSystem:RootPath"] = Path.Combine(this.tempDirectory, "FileSystem");
            config["Providers:FileSystem:Enabled"] = "true";

            // ESENT provider config
            config["Providers:Esent:Name"] = "Esent";
            config["Providers:Esent:DatabasePath"] = Path.Combine(this.tempDirectory, "Esent", "benchmark.db");
            config["Providers:Esent:InstanceName"] = "BenchmarkInstance";
            config["Providers:Esent:UseSessionPool"] = "true";
            config["Providers:Esent:Enabled"] = "true";

            // ClusterRegistry provider config
            config["Providers:ClusterRegistry:Name"] = "ClusterRegistry";
            config["Providers:ClusterRegistry:ClusterName"] = "TestCluster";
            config["Providers:ClusterRegistry:RootPath"] = @"Software\BenchmarkTests";
            config["Providers:ClusterRegistry:ApplicationName"] = "ReliableStoreBenchmark";
            config["Providers:ClusterRegistry:ServiceName"] = $"Benchmark_{Guid.NewGuid():N}";
            config["Providers:ClusterRegistry:FallbackToLocalRegistry"] = "true";
            config["Providers:ClusterRegistry:EnableCompression"] = "true";
            config["Providers:ClusterRegistry:MaxValueSizeKB"] = "64";
            config["Providers:ClusterRegistry:ConnectionTimeoutSeconds"] = "30";
            config["Providers:ClusterRegistry:RetryCount"] = "3";
            config["Providers:ClusterRegistry:RetryDelayMilliseconds"] = "100";
            config["Providers:ClusterRegistry:Enabled"] = "true";

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

        private List<Product> GenerateTestData(int count, string payloadSize)
        {
            var products = new List<Product>(count);
            var descriptionSize = payloadSize switch
            {
                "Small" => 1_000,      // ~1 KB
                "Medium" => 10_000, // ~10 KB
                "Large" => 5_000_000,    // ~5 MB
                _ => 10_000
            };

            for (int i = 0; i < count; i++)
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