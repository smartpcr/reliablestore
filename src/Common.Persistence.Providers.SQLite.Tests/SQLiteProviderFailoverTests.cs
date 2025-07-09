// -----------------------------------------------------------------------
// <copyright file="SQLiteProviderFailoverTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Providers.SQLite.Tests
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
    using Common.Persistence.Providers.SQLite;
    using Microsoft.Data.Sqlite;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Models;
    using Xunit;
    using Xunit.Abstractions;

    /// <summary>
    /// Tests for SQLite provider failover scenarios in Windows Failover Cluster environments.
    /// These tests simulate various failure conditions to ensure the database can be properly
    /// failed over between cluster nodes without data loss or corruption.
    /// </summary>
    public class SQLiteProviderFailoverTests : IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly string testDbPath;
        private readonly List<string> tempFiles = new();

        public SQLiteProviderFailoverTests(ITestOutputHelper output)
        {
            this.output = output;
            this.testDbPath = Path.Combine(Path.GetTempPath(), "failover_tests");
            Directory.CreateDirectory(this.testDbPath);
        }

        /// <summary>
        /// Tests basic failover scenario where one node (simulated by thread1) releases the database lock
        /// and another node (simulated by thread2) acquires it. This simulates a controlled failover
        /// where the primary node gracefully shuts down before the secondary takes over.
        /// </summary>
        [Fact]
        public async Task Failover_ShouldAllowNewThreadToAcquireLockAfterPreviousThreadReleases()
        {
            var dbFile = Path.Combine(this.testDbPath, $"failover_test_{Guid.NewGuid()}.db");
            this.tempFiles.Add(dbFile);

            var entity1 = new Product { Id = "1", Name = "Product 1", Price = 10.0m };
            var entity2 = new Product { Id = "2", Name = "Product 2", Price = 20.0m };

            var thread1Complete = new ManualResetEventSlim(false);
            var thread2Start = new ManualResetEventSlim(false);
            var thread1Exception = (Exception?)null;
            var thread2Exception = (Exception?)null;

            // Thread1 simulates the primary node that currently owns the database
            var thread1 = new Thread(() =>
            {
                try
                {
                    using (var provider = this.CreateProvider(dbFile, "thread1"))
                    {
                        provider.SaveAsync(entity1.Id, entity1).GetAwaiter().GetResult();
                        this.output.WriteLine($"Thread 1: Created entity {entity1.Id}");

                        // Signal thread2 that data has been written
                        thread2Start.Set();

                        // Simulate some work being done before failover
                        Thread.Sleep(1000);
                        this.output.WriteLine("Thread 1: Releasing lock");
                    } // Provider disposal here releases the SQLite file lock
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
                    Thread.Sleep(100); // Small delay to ensure file lock is fully released

                    using (var provider = this.CreateProvider(dbFile, "thread2"))
                    {
                        this.output.WriteLine("Thread 2: Acquired lock");
                        provider.SaveAsync(entity2.Id, entity2).GetAwaiter().GetResult();
                        this.output.WriteLine($"Thread 2: Created entity {entity2.Id}");

                        // Verify that data written by thread1 is still accessible
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

            var allCompleted = thread1.Join(TimeSpan.FromSeconds(10)) && thread2.Join(TimeSpan.FromSeconds(10));

            allCompleted.Should().BeTrue("Both threads should complete within timeout");
            thread1Exception.Should().BeNull("Thread 1 should not throw exception");
            thread2Exception.Should().BeNull("Thread 2 should not throw exception");
        }

        /// <summary>
        /// Tests failover scenario where the primary node terminates abruptly without properly closing
        /// the database connection. This simulates a crash or power failure on the primary node.
        /// The test verifies that SQLite's file locking mechanism allows the secondary node to take over
        /// once the process holding the lock is terminated.
        /// </summary>
        [Fact]
        public async Task Failover_ShouldHandleAbruptProcessTermination()
        {
            var dbFile = Path.Combine(this.testDbPath, $"failover_abrupt_{Guid.NewGuid()}.db");
            this.tempFiles.Add(dbFile);

            var entity1 = new Product { Id = "1", Name = "Product 1", Price = 10.0m };
            var entity2 = new Product { Id = "2", Name = "Product 2", Price = 20.0m };

            var thread1Started = new ManualResetEventSlim(false);
            var thread1ShouldTerminate = new ManualResetEventSlim(false);
            SqliteConnection? thread1Connection = null;

            // Thread1 simulates a process that crashes while holding the database lock
            var thread1 = new Thread(() =>
            {
                try
                {
                    // Open a direct connection and intentionally don't close it properly
                    var connectionString = $"Data Source={dbFile};Mode=ReadWriteCreate;";
                    thread1Connection = new SqliteConnection(connectionString);
                    thread1Connection.Open();

                    using (var cmd = thread1Connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            CREATE TABLE IF NOT EXISTS failover_Product (
                                [Key] TEXT NOT NULL PRIMARY KEY,
                                [Data] TEXT NOT NULL,
                                [Version] INTEGER NOT NULL,
                                [ETag] TEXT NULL,
                                [CreatedAt] TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                                [UpdatedAt] TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                            );
                            
                            CREATE INDEX IF NOT EXISTS IX_failover_Product_Version ON failover_Product ([Version]);
                            CREATE INDEX IF NOT EXISTS IX_failover_Product_UpdatedAt ON failover_Product ([UpdatedAt]);";
                        cmd.ExecuteNonQuery();
                    }

                    using (var cmd = thread1Connection.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO failover_Product ([Key], [Data], [Version], [CreatedAt], [UpdatedAt]) VALUES (@key, @data, @version, @createdAt, @updatedAt)";
                        cmd.Parameters.AddWithValue("@key", entity1.Id);
                        cmd.Parameters.AddWithValue("@data", Newtonsoft.Json.JsonConvert.SerializeObject(entity1));
                        cmd.Parameters.AddWithValue("@version", 0);
                        cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
                        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
                        cmd.ExecuteNonQuery();
                    }

                    this.output.WriteLine("Thread 1: Created entity and holding connection open");
                    thread1Started.Set();

                    // Wait for termination signal (simulating the process running normally before crash)
                    thread1ShouldTerminate.Wait();
                    this.output.WriteLine("Thread 1: Simulating abrupt termination - not closing connection properly");
                    // Connection is NOT closed here, simulating a crash
                }
                catch (Exception ex)
                {
                    this.output.WriteLine($"Thread 1 Exception: {ex}");
                }
            });

            thread1.Start();
            thread1Started.Wait();

            // Simulate abrupt termination by forcefully ending the thread
            thread1ShouldTerminate.Set();
            thread1.Join(TimeSpan.FromSeconds(2));

            // Clean up the connection object to release the file lock
            // In a real crash scenario, the OS would release all file handles
            thread1Connection?.Dispose();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            Thread.Sleep(500); // Give OS time to fully release the lock

            Exception? thread2Exception = null;
            var thread2Success = false;

            // Thread2 simulates the secondary node attempting to take over after detecting the crash
            var thread2 = new Thread(() =>
            {
                try
                {
                    this.output.WriteLine("Thread 2: Attempting to acquire lock after abrupt termination");

                    using (var provider = this.CreateProvider(dbFile, "thread2_recovery"))
                    {
                        this.output.WriteLine("Thread 2: Successfully opened database");

                        provider.SaveAsync(entity2.Id, entity2).GetAwaiter().GetResult();
                        this.output.WriteLine($"Thread 2: Created entity {entity2.Id}");

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
            var thread2Completed = thread2.Join(TimeSpan.FromSeconds(10));

            thread2Completed.Should().BeTrue("Thread 2 should complete within timeout");
            thread2Exception.Should().BeNull("Thread 2 should not throw exception");
            thread2Success.Should().BeTrue("Thread 2 should successfully access database");
        }

        /// <summary>
        /// Tests multiple sequential failovers to ensure data integrity is maintained across
        /// multiple node transitions. This simulates a scenario where different nodes take
        /// ownership of the database in sequence, such as during rolling updates or multiple
        /// failures in a cluster.
        /// </summary>
        [Fact]
        public async Task Failover_MultipleConcurrentFailovers_ShouldMaintainDataIntegrity()
        {
            var dbFile = Path.Combine(this.testDbPath, $"failover_concurrent_{Guid.NewGuid()}.db");
            this.tempFiles.Add(dbFile);

            const int numberOfFailovers = 5;
            const int entitiesPerThread = 10;
            var allThreadsCompleted = new CountdownEvent(numberOfFailovers);
            var exceptions = new List<Exception>();
            var successCount = 0;

            // Create multiple threads, each representing a different node in the cluster
            for (int i = 0; i < numberOfFailovers; i++)
            {
                var threadIndex = i;
                var thread = new Thread(() =>
                {
                    try
                    {
                        // Stagger thread starts to simulate sequential failovers
                        Thread.Sleep(threadIndex * 200);

                        using (var provider = this.CreateProvider(dbFile, $"thread{threadIndex}"))
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
                        allThreadsCompleted.Signal();
                    }
                });

                thread.Start();
            }

            var allCompleted = allThreadsCompleted.Wait(TimeSpan.FromSeconds(30));

            allCompleted.Should().BeTrue("All threads should complete within timeout");
            exceptions.Count.Should().Be(0, "No thread should throw exceptions");
            successCount.Should().Be(numberOfFailovers, "All threads should complete successfully");

            // Verify that all data from all failovers is preserved
            using (var provider = this.CreateProvider(dbFile, "verification"))
            {
                var allEntities = (await provider.GetAllAsync()).ToList();
                allEntities.Count.Should().Be(numberOfFailovers * entitiesPerThread,
                    "All entities from all threads should be persisted");
                this.output.WriteLine($"Final entity count: {allEntities.Count}");
            }
        }

        /// <summary>
        /// Tests that transaction rollbacks properly release database locks, allowing failover
        /// to proceed. This simulates a scenario where a node starts a transaction but fails
        /// before committing, ensuring that the rollback doesn't prevent the secondary node
        /// from taking over.
        /// </summary>
        [Fact]
        public async Task Failover_WithTransactionRollback_ShouldReleaseLockProperly()
        {
            var dbFile = Path.Combine(this.testDbPath, $"failover_rollback_{Guid.NewGuid()}.db");
            this.tempFiles.Add(dbFile);

            var entity1 = new Product { Id = "1", Name = "Product 1", Price = 10.0m };
            var entity2 = new Product { Id = "2", Name = "Product 2", Price = 20.0m };

            var thread1Complete = new ManualResetEventSlim(false);
            Exception? thread1Exception = null;

            // Thread1 simulates a transaction that gets rolled back
            var thread1 = new Thread(() =>
            {
                try
                {
                    var connectionString = $"Data Source={dbFile};Mode=ReadWriteCreate;";
                    using (var connection = new SqliteConnection(connectionString))
                    {
                        connection.Open();

                        // Start a transaction that will be rolled back
                        using (var transaction = connection.BeginTransaction())
                        {
                            using (var cmd = connection.CreateCommand())
                            {
                                cmd.Transaction = transaction;
                                cmd.CommandText = @"
                                    CREATE TABLE IF NOT EXISTS failover_Product (
                                        [Key] TEXT NOT NULL PRIMARY KEY,
                                        [Data] TEXT NOT NULL,
                                        [Version] INTEGER NOT NULL,
                                        [ETag] TEXT NULL,
                                        [CreatedAt] TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                                        [UpdatedAt] TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                                    );
                                    
                                    CREATE INDEX IF NOT EXISTS IX_failover_Product_Version ON failover_Product ([Version]);
                                    CREATE INDEX IF NOT EXISTS IX_failover_Product_UpdatedAt ON failover_Product ([UpdatedAt]);";
                                cmd.ExecuteNonQuery();
                            }

                            using (var cmd = connection.CreateCommand())
                            {
                                cmd.Transaction = transaction;
                                cmd.CommandText = "INSERT INTO failover_Product ([Key], [Data], [Version], [CreatedAt], [UpdatedAt]) VALUES (@key, @data, @version, @createdAt, @updatedAt)";
                                cmd.Parameters.AddWithValue("@key", entity1.Id);
                                cmd.Parameters.AddWithValue("@data", Newtonsoft.Json.JsonConvert.SerializeObject(entity1));
                                cmd.Parameters.AddWithValue("@version", 0);
                                cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
                                cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
                                cmd.ExecuteNonQuery();
                            }

                            this.output.WriteLine("Thread 1: Created entity in transaction, rolling back");
                            // Rollback the transaction instead of committing
                            transaction.Rollback();
                            this.output.WriteLine("Thread 1: Transaction rolled back");
                        }
                    } // Connection closed here, releasing all locks
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

            thread1.Start();
            thread1Complete.Wait();

            thread1Exception.Should().BeNull("Thread 1 should not throw exception");

            // Verify that the database is accessible and rollback worked correctly
            using (var provider = this.CreateProvider(dbFile, "mainthread"))
            {
                this.output.WriteLine("Main thread: Verifying database is accessible after rollback");

                await provider.SaveAsync(entity2.Id, entity2);
                var entities = (await provider.GetAllAsync()).ToList();

                // Should only have entity2 since entity1 was rolled back
                entities.Count.Should().Be(1, "Only one entity should exist after rollback");
                entities[0].Id.Should().Be(entity2.Id, "The entity created after rollback should exist");
            }
        }

        private ICrudStorageProvider<Product> CreateProvider(string dbFile, string providerName)
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder
                .SetMinimumLevel(LogLevel.Debug)
                .AddXunit(this.output));

            var config = new Dictionary<string, string>
            {
                [$"Providers:{providerName}:Name"] = providerName,
                [$"Providers:{providerName}:AssemblyName"] = "CRP.Common.Persistence.Providers.SQLite",
                [$"Providers:{providerName}:TypeName"] = "Common.Persistence.Providers.SQLite.SQLiteProvider`1",
                [$"Providers:{providerName}:Enabled"] = "true",
                [$"Providers:{providerName}:Capabilities"] = "1",
                [$"Providers:{providerName}:DataSource"] = dbFile,
                [$"Providers:{providerName}:Mode"] = "ReadWriteCreate",
                [$"Providers:{providerName}:Cache"] = "Shared",
                [$"Providers:{providerName}:ForeignKeys"] = "true",
                [$"Providers:{providerName}:CommandTimeout"] = "30",
                [$"Providers:{providerName}:CreateTableIfNotExists"] = "true",
                [$"Providers:{providerName}:Schema"] = "failover"
            };

            var configuration = services.AddConfiguration(config);
            var settings = configuration.GetConfiguredSettings<SQLiteProviderSettings>($"Providers:{providerName}");
            services.AddKeyedSingleton<CrudStorageProviderSettings>(providerName, (_, _) => settings);
            services.AddSingleton<IConfigReader, JsonConfigReader>();
            services.AddPersistence();

            var serviceProvider = services.BuildServiceProvider();
            var factory = serviceProvider.GetRequiredService<ICrudStorageProviderFactory>();
            return factory.Create<Product>(providerName);
        }

        public void Dispose()
        {
            foreach (var file in this.tempFiles)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                }
            }

            try
            {
                if (Directory.Exists(this.testDbPath))
                {
                    Directory.Delete(this.testDbPath, true);
                }
            }
            catch
            {
            }
        }
    }
}