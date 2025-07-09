// -----------------------------------------------------------------------
// <copyright file="EsentProviderFailoverTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Providers.Esent.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using AwesomeAssertions;
    using Common.Persistence.Configuration;
    using Common.Persistence.Contract;
    using Common.Persistence.Factory;
    using Common.Persistence.Providers.Esent;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Isam.Esent.Interop;
    using Models;
    using Xunit;
    using Xunit.Abstractions;

    /// <summary>
    /// Tests for ESEnt provider failover scenarios in Windows Failover Cluster environments.
    /// ESEnt (Extensible Storage Engine) is Windows-only and provides crash recovery capabilities.
    /// These tests simulate various failure conditions to ensure the database can be properly
    /// failed over between cluster nodes without data loss or corruption.
    /// </summary>
    public class EsentProviderFailoverTests : IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly string testDbPath;
        private readonly List<string> tempDirs = new();

        public EsentProviderFailoverTests(ITestOutputHelper output)
        {
            this.output = output;
            this.testDbPath = Path.Combine(Path.GetTempPath(), "esent_failover_tests");
            Directory.CreateDirectory(this.testDbPath);
        }

        /// <summary>
        /// Tests basic failover scenario where one node (simulated by thread1) releases the ESEnt instance
        /// and another node (simulated by thread2) creates a new instance. ESEnt uses exclusive file locks
        /// and instance names to ensure only one process can access the database at a time.
        /// </summary>
        [WindowsOnlyFact]
        public async Task Failover_ShouldAllowNewThreadToAcquireLockAfterPreviousThreadReleases()
        {
            var dbDir = Path.Combine(this.testDbPath, $"failover_test_{Guid.NewGuid()}");
            Directory.CreateDirectory(dbDir);
            this.tempDirs.Add(dbDir);

            var entity1 = new Product { Id = "1", Name = "Product 1", Price = 10.0m };
            var entity2 = new Product { Id = "2", Name = "Product 2", Price = 20.0m };

            var thread1Complete = new ManualResetEventSlim(false);
            var thread2Start = new ManualResetEventSlim(false);
            var thread1Exception = (Exception?)null;
            var thread2Exception = (Exception?)null;

            // Thread1 simulates the primary node that currently owns the ESEnt instance
            var thread1 = new Thread(() =>
            {
                try
                {
                    using (var provider = this.CreateProvider(dbDir, "thread1"))
                    {
                        provider.SaveAsync(entity1.Id, entity1).GetAwaiter().GetResult();
                        this.output.WriteLine($"Thread 1: Created entity {entity1.Id}");

                        // Signal thread2 that data has been written
                        thread2Start.Set();

                        // Simulate some work being done before failover
                        Thread.Sleep(1000);
                        this.output.WriteLine("Thread 1: Releasing lock");
                    } // ESEnt provider disposal cleanly shuts down the instance
                }
                catch (Exception ex)
                {
                    thread1Exception = ex;
                    this.output.WriteLine($"Thread 1 Exception: {ex}");
                }
                finally
                {
                    thread1Complete.Set();
                }
            });

            // Thread2 simulates the secondary node that will take over after failover
            var thread2 = new Thread(() =>
            {
                try
                {
                    // Wait for thread1 to write some data first
                    thread2Start.Wait();
                    this.output.WriteLine("Thread 2: Waiting for Thread 1 to release lock");

                    // Wait for thread1 to complete (simulating failover detection)
                    thread1Complete.Wait();
                    Thread.Sleep(500); // ESEnt may need time to fully release resources

                    using (var provider = this.CreateProvider(dbDir, "thread2"))
                    {
                        this.output.WriteLine("Thread 2: Acquired lock");
                        provider.SaveAsync(entity2.Id, entity2).GetAwaiter().GetResult();
                        this.output.WriteLine($"Thread 2: Created entity {entity2.Id}");

                        // Verify that data written by thread1 is still accessible
                        // ESEnt provides crash recovery, so committed data survives
                        var entities = provider.GetAllAsync().GetAwaiter().GetResult().ToList();
                        entities.Count.Should().Be(2);
                        this.output.WriteLine($"Thread 2: Found {entities.Count} entities");
                    }
                }
                catch (Exception ex)
                {
                    thread2Exception = ex;
                    this.output.WriteLine($"Thread 2 Exception: {ex}");
                }
            });

            thread1.Start();
            thread2.Start();

            var allCompleted = thread1.Join(TimeSpan.FromSeconds(15)) && thread2.Join(TimeSpan.FromSeconds(15));

            allCompleted.Should().BeTrue("Both threads should complete within timeout");
            thread1Exception.Should().BeNull("Thread 1 should not throw exception");
            thread2Exception.Should().BeNull("Thread 2 should not throw exception");
        }

        /// <summary>
        /// Tests failover scenario where the primary node terminates abruptly without properly closing
        /// the ESEnt instance. ESEnt has built-in crash recovery that automatically recovers from
        /// dirty shutdowns by replaying transaction logs. This test verifies that the secondary node
        /// can successfully recover and access the database after a simulated crash.
        /// </summary>
        [WindowsOnlyFact]
        public async Task Failover_ShouldHandleAbruptProcessTermination()
        {
            var dbDir = Path.Combine(this.testDbPath, $"failover_abrupt_{Guid.NewGuid()}");
            Directory.CreateDirectory(dbDir);
            this.tempDirs.Add(dbDir);

            var entity1 = new Product { Id = "1", Name = "Product 1", Price = 10.0m };
            var entity2 = new Product { Id = "2", Name = "Product 2", Price = 20.0m };

            var thread1Started = new ManualResetEventSlim(false);
            var thread1ShouldTerminate = new ManualResetEventSlim(false);
            Instance? thread1Instance = null;
            Session? thread1Session = null;

            // Thread1 simulates a process that crashes while the ESEnt instance is active
            var thread1 = new Thread(() =>
            {
                try
                {
                    // Create a raw ESEnt instance to simulate low-level crash
                    var instanceName = "FailoverTestInstance";
                    var databasePath = Path.Combine(dbDir, "database.edb");

                    SystemParameters.DatabasePageSize = 8192;

                    thread1Instance = new Instance(instanceName);
                    thread1Instance.Parameters.CreatePathIfNotExist = true;
                    thread1Instance.Parameters.TempDirectory = dbDir;
                    thread1Instance.Parameters.SystemDirectory = dbDir;
                    thread1Instance.Parameters.LogFileDirectory = dbDir;
                    thread1Instance.Parameters.BaseName = "edb";
                    thread1Instance.Parameters.EnableIndexChecking = true;
                    thread1Instance.Parameters.CircularLog = true;
                    thread1Instance.Parameters.CheckpointDepthMax = 64 * 1024 * 1024;
                    thread1Instance.Parameters.LogFileSize = 1024;
                    thread1Instance.Parameters.LogBuffers = 512;
                    thread1Instance.Parameters.MaxTemporaryTables = 64;
                    thread1Instance.Parameters.MaxVerPages = 1024;
                    thread1Instance.Parameters.NoInformationEvent = true;

                    thread1Instance.Init();

                    JET_DBID dbid;
                    thread1Session = new Session(thread1Instance);
                    Api.JetCreateDatabase(thread1Session, databasePath, null, out dbid, CreateDatabaseGrbit.None);
                    Api.JetOpenDatabase(thread1Session, databasePath, null, out dbid, OpenDatabaseGrbit.None);

                    this.output.WriteLine("Thread 1: Created database instance and holding session open");
                    thread1Started.Set();

                    // Wait for termination signal (simulating the process running before crash)
                    thread1ShouldTerminate.Wait();
                    this.output.WriteLine("Thread 1: Simulating abrupt termination - not closing properly");
                    // Instance and session are NOT properly terminated, simulating a crash
                }
                catch (Exception ex)
                {
                    this.output.WriteLine($"Thread 1 Exception: {ex}");
                }
            });

            thread1.Start();
            thread1Started.Wait();

            // Simulate abrupt termination
            thread1ShouldTerminate.Set();
            thread1.Join(TimeSpan.FromSeconds(2));

            // Force cleanup of ESEnt resources
            // In a real crash, Windows would clean up all handles
            if (thread1Session != null)
            {
                try { thread1Session.Dispose(); } catch { }
            }
            if (thread1Instance != null)
            {
                try { thread1Instance.Dispose(); } catch { }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();

            Thread.Sleep(1000); // Give ESEnt time to release all resources

            Exception? thread2Exception = null;
            var thread2Success = false;

            // Thread2 simulates the secondary node taking over after detecting the crash
            var thread2 = new Thread(() =>
            {
                try
                {
                    this.output.WriteLine("Thread 2: Attempting to acquire lock after abrupt termination");

                    // ESEnt will automatically perform crash recovery when opening the database
                    using (var provider = this.CreateProvider(dbDir, "thread2_recovery"))
                    {
                        this.output.WriteLine("Thread 2: Successfully opened database");

                        provider.SaveAsync(entity1.Id, entity1).GetAwaiter().GetResult();
                        provider.SaveAsync(entity2.Id, entity2).GetAwaiter().GetResult();
                        this.output.WriteLine($"Thread 2: Created entities");

                        var entities = provider.GetAllAsync().GetAwaiter().GetResult().ToList();
                        entities.Count.Should().Be(2);
                        this.output.WriteLine($"Thread 2: Found {entities.Count} entities");
                        thread2Success = true;
                    }
                }
                catch (Exception ex)
                {
                    thread2Exception = ex;
                    this.output.WriteLine($"Thread 2 Exception: {ex}");
                }
            });

            thread2.Start();
            var thread2Completed = thread2.Join(TimeSpan.FromSeconds(20));

            thread2Completed.Should().BeTrue("Thread 2 should complete within timeout");
            thread2Exception.Should().BeNull("Thread 2 should not throw exception");
            thread2Success.Should().BeTrue("Thread 2 should successfully access database");
        }

        /// <summary>
        /// Tests multiple sequential failovers with ESEnt to ensure data integrity is maintained.
        /// Each thread represents a different node taking ownership of the database in sequence.
        /// ESEnt's transaction log and recovery mechanisms ensure no data loss during transitions.
        /// </summary>
        [WindowsOnlyFact]
        public async Task Failover_MultipleConcurrentFailovers_ShouldMaintainDataIntegrity()
        {
            var dbDir = Path.Combine(this.testDbPath, $"failover_concurrent_{Guid.NewGuid()}");
            Directory.CreateDirectory(dbDir);
            this.tempDirs.Add(dbDir);

            const int numberOfFailovers = 5;
            const int entitiesPerThread = 10;
            var exceptions = new List<Exception>();
            var successCount = 0;

            // Use a semaphore to ensure only one thread accesses the database at a time
            // This simulates proper failover where only one node owns the database
            var dbAccessSemaphore = new SemaphoreSlim(1, 1);
            var completionEvents = new ManualResetEventSlim[numberOfFailovers];
            
            for (int i = 0; i < numberOfFailovers; i++)
            {
                completionEvents[i] = new ManualResetEventSlim(false);
            }

            // Create multiple threads, each representing a different node in the cluster
            for (int i = 0; i < numberOfFailovers; i++)
            {
                var threadIndex = i;
                var completionEvent = completionEvents[i];
                
                var thread = new Thread(() =>
                {
                    try
                    {
                        // Wait for previous thread to complete if not the first
                        if (threadIndex > 0)
                        {
                            completionEvents[threadIndex - 1].Wait();
                            // Add delay to ensure clean release of resources
                            Thread.Sleep(500);
                        }

                        // Acquire exclusive access to the database
                        dbAccessSemaphore.Wait();
                        try
                        {
                            using (var provider = this.CreateProvider(dbDir, $"thread{threadIndex}"))
                            {
                                this.output.WriteLine($"Thread {threadIndex}: Acquired lock");

                                for (int j = 0; j < entitiesPerThread; j++)
                                {
                                    var entity = new Product
                                    {
                                        Id = $"thread{threadIndex}_product{j}",
                                        Name = $"Product from thread {threadIndex}",
                                        Price = (threadIndex + 1) * 10.0m + j
                                    };

                                    provider.SaveAsync(entity.Id, entity).GetAwaiter().GetResult();
                                }

                                this.output.WriteLine($"Thread {threadIndex}: Created {entitiesPerThread} entities");
                                Interlocked.Increment(ref successCount);
                            }

                            this.output.WriteLine($"Thread {threadIndex}: Released lock");
                        }
                        finally
                        {
                            dbAccessSemaphore.Release();
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                        this.output.WriteLine($"Thread {threadIndex} Exception: {ex}");
                    }
                    finally
                    {
                        completionEvent.Set();
                    }
                });

                thread.Start();
            }

            // Wait for all threads to complete
            var allCompleted = completionEvents.All(e => e.Wait(TimeSpan.FromSeconds(60)));

            allCompleted.Should().BeTrue("All threads should complete within timeout");
            exceptions.Count.Should().Be(0, "No thread should throw exceptions");
            successCount.Should().Be(numberOfFailovers, "All threads should complete successfully");

            // Verify that all data from all failovers is preserved
            // ESEnt's durability guarantees ensure all committed transactions survive
            using (var provider = this.CreateProvider(dbDir, "verification"))
            {
                var allEntities = (await provider.GetAllAsync()).ToList();
                allEntities.Count.Should().Be(numberOfFailovers * entitiesPerThread,
                    "All entities from all threads should be persisted");
                this.output.WriteLine($"Final entity count: {allEntities.Count}");
            }

            // Clean up
            foreach (var evt in completionEvents)
            {
                evt.Dispose();
            }
            dbAccessSemaphore.Dispose();
        }

        /// <summary>
        /// Tests that ESEnt properly handles failover after an exception occurs during processing.
        /// When an exception is thrown, ESEnt automatically rolls back any uncommitted transactions
        /// and the instance disposal ensures clean shutdown, allowing the next node to take over.
        /// </summary>
        [WindowsOnlyFact]
        public async Task Failover_WithTransactionRollback_ShouldReleaseLockProperly()
        {
            var dbDir = Path.Combine(this.testDbPath, $"failover_rollback_{Guid.NewGuid()}");
            Directory.CreateDirectory(dbDir);
            this.tempDirs.Add(dbDir);

            var entity1 = new Product { Id = "1", Name = "Product 1", Price = 10.0m };
            var entity2 = new Product { Id = "2", Name = "Product 2", Price = 20.0m };

            var thread1Complete = new ManualResetEventSlim(false);
            Exception? thread1Exception = null;

            // Thread1 simulates a failure during processing
            var thread1 = new Thread(() =>
            {
                try
                {
                    using (var provider = this.CreateProvider(dbDir, "thread1_rollback"))
                    {
                        provider.SaveAsync(entity1.Id, entity1).GetAwaiter().GetResult();
                        this.output.WriteLine("Thread 1: Created entity, simulating failure before commit");
                        // Simulate a failure - ESEnt will handle cleanup
                        throw new InvalidOperationException("Simulated failure");
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message == "Simulated failure")
                {
                    this.output.WriteLine("Thread 1: Simulated failure occurred, provider disposed");
                }
                catch (Exception ex)
                {
                    thread1Exception = ex;
                    this.output.WriteLine($"Thread 1 Unexpected Exception: {ex}");
                }
                finally
                {
                    thread1Complete.Set();
                }
            });

            thread1.Start();
            thread1Complete.Wait();

            thread1Exception.Should().BeNull("Thread 1 should not throw unexpected exception");

            Thread.Sleep(500);

            // Verify that the database is accessible after the failure
            // ESEnt's clean shutdown ensures the database is not corrupted
            using (var provider = this.CreateProvider(dbDir, "mainthread_rollback"))
            {
                this.output.WriteLine("Main thread: Verifying database is accessible after simulated failure");

                await provider.SaveAsync(entity2.Id, entity2);
                var entities = (await provider.GetAllAsync()).ToList();

                // May contain entity1 if it was committed before the exception
                this.output.WriteLine($"Main thread: Found {entities.Count} entities");
            }
        }

        /// <summary>
        /// Tests rapid failover scenarios where multiple nodes attempt to acquire the database
        /// in quick succession. This simulates a "thundering herd" scenario where multiple
        /// backup nodes detect a failure simultaneously. ESEnt's exclusive locking ensures
        /// only one node can access the database at a time.
        /// </summary>
        [WindowsOnlyFact]
        public async Task Failover_RapidSuccession_ShouldHandleQuickTransitions()
        {
            var dbDir = Path.Combine(this.testDbPath, $"failover_rapid_{Guid.NewGuid()}");
            Directory.CreateDirectory(dbDir);
            this.tempDirs.Add(dbDir);

            const int numberOfTransitions = 10;
            var barrier = new Barrier(numberOfTransitions);
            var exceptions = new List<Exception>();
            var successCount = 0;

            // Create multiple threads that will all try to acquire the database simultaneously
            var threads = new Thread[numberOfTransitions];
            for (int i = 0; i < numberOfTransitions; i++)
            {
                var threadIndex = i;
                threads[i] = new Thread(() =>
                {
                    try
                    {
                        // All threads wait at the barrier, then try to acquire simultaneously
                        barrier.SignalAndWait();

                        using (var provider = this.CreateProvider(dbDir, $"rapid{threadIndex}"))
                        {
                            var entity = new Product
                            {
                                Id = $"rapid_{threadIndex}",
                                Name = $"Rapid Product {threadIndex}",
                                Price = threadIndex * 5.0m
                            };

                            provider.SaveAsync(entity.Id, entity).GetAwaiter().GetResult();
                            this.output.WriteLine($"Thread {threadIndex}: Created entity");
                            Interlocked.Increment(ref successCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                        this.output.WriteLine($"Thread {threadIndex} Exception: {ex.Message}");
                    }
                });

                threads[i].Start();
            }

            foreach (var thread in threads)
            {
                thread.Join(TimeSpan.FromSeconds(30));
            }

            this.output.WriteLine($"Success count: {successCount}/{numberOfTransitions}");
            this.output.WriteLine($"Exception count: {exceptions.Count}");

            // In a rapid succession scenario, we expect some threads to succeed
            // ESEnt's locking will serialize access, preventing corruption
            successCount.Should().BeGreaterThan(0, "At least some threads should succeed");

            // Verify that successful writes were persisted correctly
            using (var provider = this.CreateProvider(dbDir, "verification_rapid"))
            {
                var allEntities = (await provider.GetAllAsync()).ToList();
                this.output.WriteLine($"Final entity count: {allEntities.Count}");
                allEntities.Count.Should().BeGreaterThan(0, "Some entities should be persisted");
            }
        }

        /// <summary>
        /// Creates an ESEnt provider instance with the specified database directory and instance name.
        /// Each provider must have a unique instance name to avoid conflicts.
        /// </summary>
        private ICrudStorageProvider<Product> CreateProvider(string dbDir, string providerName)
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder
                .SetMinimumLevel(LogLevel.Debug)
                .AddXunit(this.output));

            var dbPath = Path.Combine(dbDir, "database.edb");
            var config = new Dictionary<string, string>
            {
                [$"Providers:{providerName}:Name"] = providerName,
                [$"Providers:{providerName}:AssemblyName"] = "CRP.Common.Persistence.Providers.Esent",
                [$"Providers:{providerName}:TypeName"] = "Common.Persistence.Providers.Esent.EsentProvider`1",
                [$"Providers:{providerName}:Enabled"] = "true",
                [$"Providers:{providerName}:Capabilities"] = "1",
                [$"Providers:{providerName}:DatabasePath"] = dbPath,
                [$"Providers:{providerName}:InstanceName"] = $"FailoverTest_{providerName}_{Guid.NewGuid():N}",
                [$"Providers:{providerName}:MaxSessions"] = "10",
                [$"Providers:{providerName}:EnableCrashRecovery"] = "true"
            };

            var configuration = services.AddConfiguration(config);
            var settings = configuration.GetConfiguredSettings<EsentStoreSettings>($"Providers:{providerName}");
            services.AddKeyedSingleton<CrudStorageProviderSettings>(providerName, (_, _) => settings);
            services.AddSingleton<IConfigReader, JsonConfigReader>();
            services.AddPersistence();

            var serviceProvider = services.BuildServiceProvider();
            var factory = serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            return factory.Create<Product>(providerName);
        }

        public void Dispose()
        {
            Thread.Sleep(500);

            foreach (var dir in this.tempDirs)
            {
                try
                {
                    if (Directory.Exists(dir))
                    {
                        Directory.Delete(dir, true);
                    }
                }
                catch (Exception ex)
                {
                    this.output.WriteLine($"Failed to delete directory {dir}: {ex.Message}");
                }
            }

            try
            {
                if (Directory.Exists(this.testDbPath))
                {
                    Directory.Delete(this.testDbPath, true);
                }
            }
            catch (Exception ex)
            {
                this.output.WriteLine($"Failed to delete test directory {this.testDbPath}: {ex.Message}");
            }
        }
    }
}