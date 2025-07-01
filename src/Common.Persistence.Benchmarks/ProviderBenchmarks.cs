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
    using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
    using Common.Persistence.Configuration;
    using Common.Persistence.Contract;
    using Common.Persistence.Factory;
    using Common.Persistence.Providers.Esent;
    using Common.Persistence.Providers.ClusterRegistry;
    using Common.Persistence.Providers.InMemory;
    using Microsoft.Extensions.Configuration;
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

        [Params(1000, 10000, 100000)]
        public int OperationCount { get; set; }

        [Params("Small", "Medium", "Large")]
        public string PayloadSize { get; set; }

        [Params("Esent", "ClusterRegistry")]
        public string ProviderType { get; set; }

        [Params(2, 4, 8, 16)]
        public int CoreCount { get; set; }

        [GlobalSetup]
        public void GlobalSetup()
        {
            // Skip non-Windows providers on non-Windows platforms
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && 
                (ProviderType == "Esent" || ProviderType == "ClusterRegistry"))
            {
                return;
            }

            // Setup temp directory
            tempDirectory = Path.Combine(Path.GetTempPath(), $"ReliableStoreBenchmark_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            // Setup DI container
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
            
            // Add configuration
            var configuration = services.AddConfiguration(this.GetProviderConfiguration());
            var esentSettings = configuration.GetConfiguredSettings<EsentStoreSettings>($"Providers:Esent");
            services.AddKeyedSingleton<CrudStorageProviderSettings>("Esent", (_, _) => esentSettings);
            var clusterRegistrySettings = configuration.GetConfiguredSettings<ClusterRegistryStoreSettings>($"Providers:ClusterRegistry");
            services.AddKeyedSingleton<CrudStorageProviderSettings>("ClusterRegistry", (_, _) => clusterRegistrySettings);

            // Register providers
            services.AddPersistence();

            serviceProvider = services.BuildServiceProvider();

            // Generate test data
            testData = GenerateTestData(OperationCount, PayloadSize);

            // Create provider
            var factory = serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            provider = factory.Create<Product>(ProviderType);

            // Set CPU affinity
            SetCpuAffinity(CoreCount);
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            provider?.Dispose();
            
            if (serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }

            // Cleanup temp directory
            if (Directory.Exists(tempDirectory))
            {
                try
                {
                    Directory.Delete(tempDirectory, true);
                }
                catch { }
            }

            // Cleanup registry for ClusterRegistry provider
            if (ProviderType == "ClusterRegistry" && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(@"Software\BenchmarkTests", false);
                }
                catch { }
            }
        }

        [Benchmark(Description = "Sequential Write Operations")]
        public async Task SequentialWrites()
        {
            foreach (var product in testData)
            {
                await provider.SaveAsync(product.Id, product);
            }
        }

        [Benchmark(Description = "Sequential Read Operations")]
        public async Task SequentialReads()
        {
            // First write all data
            foreach (var product in testData)
            {
                await provider.SaveAsync(product.Id, product);
            }

            // Then read all data
            foreach (var product in testData)
            {
                var result = await provider.GetAsync(product.Id);
            }
        }

        [Benchmark(Description = "Mixed Operations (70% Read, 20% Write, 10% Delete)")]
        public async Task MixedOperations()
        {
            // First write initial data (50% of total)
            var initialCount = testData.Count / 2;
            for (int i = 0; i < initialCount; i++)
            {
                await provider.SaveAsync(testData[i].Id, testData[i]);
            }

            // Perform mixed operations
            var random = new Random(42);
            for (int i = 0; i < testData.Count; i++)
            {
                var operation = random.Next(100);
                
                if (operation < 70) // 70% reads
                {
                    var index = random.Next(initialCount);
                    await provider.GetAsync(testData[index].Id);
                }
                else if (operation < 90) // 20% writes
                {
                    var index = initialCount + (i % (testData.Count - initialCount));
                    await provider.SaveAsync(testData[index].Id, testData[index]);
                }
                else // 10% deletes
                {
                    var index = random.Next(initialCount);
                    await provider.DeleteAsync(testData[index].Id);
                }
            }
        }

        [Benchmark(Description = "Batch Operations")]
        public async Task BatchOperations()
        {
            const int batchSize = 100;
            
            // Process in batches
            for (int i = 0; i < testData.Count; i += batchSize)
            {
                var batch = testData.Skip(i).Take(batchSize).ToList();
                var tasks = batch.Select(p => provider.SaveAsync(p.Id, p)).ToArray();
                await Task.WhenAll(tasks);
            }
        }

        [Benchmark(Description = "GetAll Operation")]
        public async Task GetAllOperation()
        {
            // First write all data
            foreach (var product in testData)
            {
                await provider.SaveAsync(product.Id, product);
            }

            // Then get all
            var all = await provider.GetAllAsync();
            var count = all.Count();
        }

        private Dictionary<string, string> GetProviderConfiguration()
        {
            var config = new Dictionary<string, string>();

            // InMemory provider config
            config["Providers:InMemory:Name"] = "InMemory";
            config["Providers:InMemory:Enabled"] = "true";

            // FileSystem provider config
            config["Providers:FileSystem:Name"] = "FileSystem";
            config["Providers:FileSystem:RootPath"] = Path.Combine(tempDirectory, "FileSystem");
            config["Providers:FileSystem:Enabled"] = "true";

            // ESENT provider config
            config["Providers:Esent:Name"] = "Esent";
            config["Providers:Esent:DatabasePath"] = Path.Combine(tempDirectory, "Esent", "benchmark.db");
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

            return config;
        }

        private List<Product> GenerateTestData(int count, string payloadSize)
        {
            var products = new List<Product>(count);
            var descriptionSize = payloadSize switch
            {
                "Small" => 1000,      // ~1 KB
                "Medium" => 10000,    // ~10 KB
                "Large" => 100000,    // ~100 KB
                _ => 1000
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
                AddDiagnoser(MemoryDiagnoser.Default);
                AddDiagnoser(ThreadingDiagnoser.Default);
                
                // Use InProcessNoEmitToolchain for better compatibility
                AddJob(Job.Default
                    .WithToolchain(InProcessNoEmitToolchain.Instance)
                    .WithStrategy(RunStrategy.Throughput));
            }
        }
    }
}