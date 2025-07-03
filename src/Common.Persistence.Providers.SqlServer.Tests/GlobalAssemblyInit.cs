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
        GlobalAssemblyInit.InitializeAsync().GetAwaiter().GetResult();
    }

    private static async Task InitializeAsync()
    {
        await GlobalAssemblyInit.initializationSemaphore.WaitAsync();
        try
        {
            if (GlobalAssemblyInit.isInitialized)
            {
                return;
            }

            // Load environment variables
            var envPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
            if (File.Exists(envPath))
            {
                Env.Load(envPath);
            }

            // Start Docker Compose
            await GlobalAssemblyInit.StartDockerComposeAsync();

            GlobalAssemblyInit.isInitialized = true;

            // Register cleanup on app domain unload
            AppDomain.CurrentDomain.ProcessExit += GlobalAssemblyInit.OnProcessExit;
        }
        finally
        {
            GlobalAssemblyInit.initializationSemaphore.Release();
        }
    }

    private static async Task StartDockerComposeAsync()
    {
        var containerRuntime = GlobalAssemblyInit.GetContainerRuntime();
        var composeCommand = "compose";
        var workingDirectory = AppDomain.CurrentDomain.BaseDirectory;

        // Check if container is already running
        var checkProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = containerRuntime,
                Arguments = "ps --filter name=reliablestore-sqlserver-tests --format \"{{.Names}}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            }
        };

        checkProcess.Start();
        var output = await checkProcess.StandardOutput.ReadToEndAsync();
        await checkProcess.WaitForExitAsync();

        if (output.Contains("reliablestore-sqlserver-tests"))
        {
            Console.WriteLine("SQL Server container is already running.");
            return;
        }

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