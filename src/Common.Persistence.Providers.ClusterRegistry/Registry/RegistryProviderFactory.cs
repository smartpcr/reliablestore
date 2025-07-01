//-------------------------------------------------------------------------------
// <copyright file="RegistryProviderFactory.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Registry
{
    using System;
    using System.Runtime.Versioning;
    using Common.Persistence.Providers.ClusterRegistry.Api;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Factory for creating registry providers.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class RegistryProviderFactory
    {
        /// <summary>
        /// Creates a registry provider based on availability.
        /// Tries cluster registry first, falls back to local registry if cluster is not available.
        /// </summary>
        public static IRegistryProvider Create(string? clusterName, string rootPath, ILogger logger)
        {
            // Try to create cluster registry provider first
            if (!string.IsNullOrEmpty(clusterName) || IsClusterEnvironment())
            {
                try
                {
                    var clusterProvider = new ClusterRegistryProviderAdapter(clusterName);
                    logger.LogInformation("Using Cluster Registry provider for cluster '{ClusterName}'", clusterName ?? "local");
                    return clusterProvider;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to create Cluster Registry provider, falling back to local Windows Registry");
                }
            }

            // Fall back to local registry
            logger.LogInformation("Using local Windows Registry provider");
            return new LocalRegistryProvider(rootPath);
        }

        private static bool IsClusterEnvironment()
        {
            try
            {
                // Check if we're running in a cluster environment by trying to open the local cluster
                using (var handle = SafeClusterHandle.Open(null))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}