//-------------------------------------------------------------------------------
// <copyright file="ClusterRegistryProviderAdapter.cs" company="Microsoft Corp.">
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
    /// Registry provider that uses Windows Failover Cluster Registry.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal class ClusterRegistryProviderAdapter : IRegistryProvider
    {
        private readonly SafeClusterHandle clusterHandle;
        private readonly SafeClusterKeyHandle rootKeyHandle;
        private readonly ILogger? logger;
        private bool disposed;

        public ClusterRegistryProviderAdapter(string? clusterName, ILogger? logger = null)
        {
            this.logger = logger;
            try
            {
                this.clusterHandle = SafeClusterHandle.Open(clusterName);
                this.rootKeyHandle = this.clusterHandle.GetRootKey();
            }
            catch
            {
                this.clusterHandle?.Dispose();
                throw;
            }
        }

        public bool IsCluster => true;

        public IRegistryKey GetOrCreateKey(string keyPath)
        {
            this.ThrowIfDisposed();

            var pathParts = keyPath.Split('\\');
            SafeClusterKeyHandle currentKey = this.rootKeyHandle;
            bool shouldDisposeCurrentKey = false;

            foreach (var part in pathParts)
            {
                if (string.IsNullOrEmpty(part)) continue;

                var newKey = currentKey.CreateOrOpenSubKey(part);

                if (shouldDisposeCurrentKey)
                {
                    currentKey.Dispose();
                }

                currentKey = newKey;
                shouldDisposeCurrentKey = true;
            }

            return new ClusterRegistryKey(currentKey, keyPath, this.logger);
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(ClusterRegistryProviderAdapter));
            }
        }

        public void Dispose()
        {
            if (!this.disposed)
            {
                this.rootKeyHandle?.Dispose();
                this.clusterHandle?.Dispose();
                this.disposed = true;
            }
        }
    }
}