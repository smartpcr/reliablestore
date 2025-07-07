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
    using System.Threading;
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
    using DotNetEnv;
    using Microsoft.Extensions.Configuration;
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

        [Params(1000, 10000)]
        public int OperationCount { get; set; }

        [Params("Small", "Large")]
        public string PayloadSize { get; set; }

        [Params("InMemory", "FileSystem", "Esent", "ClusterRegistry")]
        public string ProviderType { get; set; }

        [Params(2, 8, 16)]
        public int ThreadCount { get; set; }

        [GlobalSetup]
        public void GlobalSetup()
        {
            Env.Load();

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
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(GetProviderConfiguration())
                .Build();
            services.AddSingleton<IConfiguration>(config);

            // Register providers
            services.AddPersistence();

            serviceProvider = services.BuildServiceProvider();

            // Generate test data
            testData = GenerateTestData(OperationCount, PayloadSize);

            // Create provider
            var factory = serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            provider = factory.Create<Product>(ProviderType);
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

        [Benchmark(Description = "Concurrent Write Operations")]
        public async Task ConcurrentWrites()
        {
            var itemsPerThread = OperationCount / ThreadCount;
            var tasks = new Task[ThreadCount];

            for (int t = 0; t < ThreadCount; t++)
            {
                var threadId = t;
                tasks[t] = Task.Run(async () =>
                {
                    var start = threadId * itemsPerThread;
                    var end = (threadId == ThreadCount - 1) ? OperationCount : start + itemsPerThread;
                    
                    for (int i = start; i < end; i++)
                    {
                        await provider.SaveAsync(testData[i].Id, testData[i]);
                    }
                });
            }

            await Task.WhenAll(tasks);
        }

        [Benchmark(Description = "Concurrent Read Operations")]
        public async Task ConcurrentReads()
        {
            // First write all data sequentially
            foreach (var product in testData)
            {
                await provider.SaveAsync(product.Id, product);
            }

            // Then read concurrently
            var itemsPerThread = OperationCount / ThreadCount;
            var tasks = new Task[ThreadCount];

            for (int t = 0; t < ThreadCount; t++)
            {
                var threadId = t;
                tasks[t] = Task.Run(async () =>
                {
                    var start = threadId * itemsPerThread;
                    var end = (threadId == ThreadCount - 1) ? OperationCount : start + itemsPerThread;
                    
                    for (int i = start; i < end; i++)
                    {
                        var result = await provider.GetAsync(testData[i].Id);
                    }
                });
            }

            await Task.WhenAll(tasks);
        }

        [Benchmark(Description = "Concurrent Mixed Operations")]
        public async Task ConcurrentMixedOperations()
        {
            // First write initial data (50% of total)
            var initialCount = testData.Count / 2;
            for (int i = 0; i < initialCount; i++)
            {
                await provider.SaveAsync(testData[i].Id, testData[i]);
            }

            // Perform concurrent mixed operations
            var operationsPerThread = OperationCount / ThreadCount;
            var tasks = new Task[ThreadCount];

            for (int t = 0; t < ThreadCount; t++)
            {
                var threadId = t;
                tasks[t] = Task.Run(async () =>
                {
                    var random = new Random(42 + threadId);
                    
                    for (int i = 0; i < operationsPerThread; i++)
                    {
                        var operation = random.Next(100);
                        
                        if (operation < 70) // 70% reads
                        {
                            var index = random.Next(initialCount);
                            await provider.GetAsync(testData[index].Id);
                        }
                        else if (operation < 90) // 20% writes
                        {
                            var index = initialCount + ((threadId * operationsPerThread + i) % (testData.Count - initialCount));
                            await provider.SaveAsync(testData[index].Id, testData[index]);
                        }
                        else // 10% deletes
                        {
                            var index = random.Next(initialCount);
                            try
                            {
                                await provider.DeleteAsync(testData[index].Id);
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
                "Small" => 100,      // ~100 bytes
                "Large" => 10000,    // ~10 KB
                _ => 100
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
                AddDiagnoser(MemoryDiagnoser.Default);
                AddDiagnoser(ThreadingDiagnoser.Default);
                
                // Use default toolchain (out-of-process) for long-running benchmarks
                // This avoids timeout issues with ESENT provider
                AddJob(Job.Default
                    .WithStrategy(RunStrategy.Throughput));
            }
        }
    }
}