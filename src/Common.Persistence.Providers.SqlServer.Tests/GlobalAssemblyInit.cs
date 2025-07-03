// -----------------------------------------------------------------------
// <copyright file="GlobalAssemblyInit.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Providers.SqlServer.Tests;

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DotNetEnv;

public static class GlobalAssemblyInit
{
    private static readonly SemaphoreSlim initializationSemaphore = new(1, 1);
    private static bool isInitialized;
    private static Process? composeProcess;

    [ModuleInitializer]
    public static void Initialize()
    {
        try
        {
            Console.WriteLine("GlobalAssemblyInit: Starting initialization...");
            // Load environment variables synchronously
            var envPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
            if (File.Exists(envPath))
            {
                Env.Load(envPath);
                Console.WriteLine($"GlobalAssemblyInit: Loaded .env from {envPath}");
            }
            
            // Start Docker Compose in background - don't wait
            Task.Run(async () => 
            {
                try
                {
                    await StartDockerComposeAsync();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"GlobalAssemblyInit: Background error: {ex.Message}");
                }
            });
            
            // Register cleanup on app domain unload
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            AppDomain.CurrentDomain.DomainUnload += (s, e) => OnProcessExit(s, e);
            
            Console.WriteLine("GlobalAssemblyInit: Initialization completed.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"GlobalAssemblyInit: Error during initialization: {ex}");
            throw;
        }
    }


    private static async Task StartDockerComposeAsync()
    {
        var containerRuntime = GlobalAssemblyInit.GetContainerRuntime();
        var composeCommand = "compose";
        var workingDirectory = AppDomain.CurrentDomain.BaseDirectory;

        // Check if container is already running and database exists
        if (await IsContainerAndDatabaseReady(containerRuntime))
        {
            Console.WriteLine("GlobalAssemblyInit: SQL Server container and database are already ready.");
            return;
        }

        Console.WriteLine("GlobalAssemblyInit: Container or database not ready, starting Docker Compose...");
        
        // Start Docker Compose
        GlobalAssemblyInit.composeProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = containerRuntime,
                Arguments = $"{composeCommand} up -d",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            }
        };

        GlobalAssemblyInit.composeProcess.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Console.WriteLine($"[Docker Compose] {e.Data}");
            }
        };

        GlobalAssemblyInit.composeProcess.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Console.Error.WriteLine($"[Docker Compose Error] {e.Data}");
            }
        };

        Console.WriteLine($"Starting SQL Server using {containerRuntime} compose...");
        GlobalAssemblyInit.composeProcess.Start();
        GlobalAssemblyInit.composeProcess.BeginOutputReadLine();
        GlobalAssemblyInit.composeProcess.BeginErrorReadLine();

        // Wait for the container to be healthy
        await GlobalAssemblyInit.WaitForContainerHealthyAsync(containerRuntime);
    }

    private static async Task WaitForContainerHealthyAsync(string containerRuntime)
    {
        const int maxRetries = 30;
        const int delaySeconds = 2;

        Console.WriteLine("Waiting for SQL Server to be healthy...");

        for (var i = 0; i < maxRetries; i++)
        {
            var healthProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = containerRuntime,
                    Arguments = "inspect reliablestore-sqlserver-tests --format \"{{.State.Health.Status}}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            healthProcess.Start();
            var healthStatus = await healthProcess.StandardOutput.ReadToEndAsync();
            await healthProcess.WaitForExitAsync();

            if (healthStatus.Trim().Equals("healthy", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("SQL Server is healthy and ready!");
                return;
            }

            Console.WriteLine($"SQL Server health check {i + 1}/{maxRetries}: {healthStatus.Trim()}");
            await Task.Delay(delaySeconds * 1000);
        }

        throw new InvalidOperationException("SQL Server failed to become healthy within the timeout period.");
    }

    private static async Task<bool> IsContainerAndDatabaseReady(string containerRuntime)
    {
        try
        {
            // First check if container exists and is running
            var checkContainerProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = containerRuntime,
                    Arguments = "ps --filter name=reliablestore-sqlserver-tests --format \"{{.State}}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            checkContainerProcess.Start();
            var containerState = await checkContainerProcess.StandardOutput.ReadToEndAsync();
            await checkContainerProcess.WaitForExitAsync();

            if (!containerState.Contains("running"))
            {
                Console.WriteLine("GlobalAssemblyInit: Container not running.");
                return false;
            }

            Console.WriteLine("GlobalAssemblyInit: Container is running, checking health...");

            // Check if container is healthy
            var healthProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = containerRuntime,
                    Arguments = "inspect reliablestore-sqlserver-tests --format \"{{.State.Health.Status}}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            healthProcess.Start();
            var healthStatus = await healthProcess.StandardOutput.ReadToEndAsync();
            await healthProcess.WaitForExitAsync();

            if (!healthStatus.Trim().Equals("healthy", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"GlobalAssemblyInit: Container not healthy: {healthStatus.Trim()}");
                return false;
            }

            Console.WriteLine("GlobalAssemblyInit: Container is healthy, checking database...");

            // Check if database exists
            var password = Environment.GetEnvironmentVariable("SA_PASSWORD") ?? "YourStrong@Passw0rd";
            var checkDbProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = containerRuntime,
                    Arguments = $"exec reliablestore-sqlserver-tests /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"{password}\" -C -Q \"IF DB_ID(N'ReliableStoreTest') IS NOT NULL SELECT 'EXISTS' ELSE SELECT 'NOT_EXISTS'\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            checkDbProcess.Start();
            var dbOutput = await checkDbProcess.StandardOutput.ReadToEndAsync();
            await checkDbProcess.WaitForExitAsync();

            var dbExists = dbOutput.Contains("EXISTS") && !dbOutput.Contains("NOT_EXISTS");
            Console.WriteLine($"GlobalAssemblyInit: Database exists: {dbExists}");

            return dbExists;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GlobalAssemblyInit: Error checking container/database status: {ex.Message}");
            return false;
        }
    }

    private static string GetContainerRuntime()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Check if podman is available
            var podmanCheck = Process.Start(new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "podman",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            podmanCheck?.WaitForExit();
            if (podmanCheck?.ExitCode == 0)
            {
                return "podman";
            }
        }

        return "docker";
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        GlobalAssemblyInit.StopDockerCompose();
    }

    public static void StopDockerCompose()
    {
        try
        {
            var containerRuntime = GlobalAssemblyInit.GetContainerRuntime();
            var composeCommand = "compose";
            var workingDirectory = AppDomain.CurrentDomain.BaseDirectory;

            var stopProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = containerRuntime,
                    Arguments = $"{composeCommand} down -v",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory
                }
            };

            Console.WriteLine("Stopping SQL Server container...");
            stopProcess.Start();
            stopProcess.WaitForExit(TimeSpan.FromSeconds(30));

            GlobalAssemblyInit.composeProcess?.Kill();
            GlobalAssemblyInit.composeProcess?.Dispose();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error stopping Docker Compose: {ex.Message}");
        }
    }
}