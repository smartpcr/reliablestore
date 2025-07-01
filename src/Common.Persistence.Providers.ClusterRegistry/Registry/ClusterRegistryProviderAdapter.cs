//-------------------------------------------------------------------------------
// <copyright file="ClusterRegistryProviderAdapter.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Registry
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.Versioning;
    using Common.Persistence.Providers.ClusterRegistry.Api;

    /// <summary>
    /// Registry provider that uses Windows Failover Cluster Registry.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal class ClusterRegistryProviderAdapter : IRegistryProvider
    {
        private readonly SafeClusterHandle clusterHandle;
        private readonly SafeClusterKeyHandle rootKeyHandle;
        private bool disposed;

        public ClusterRegistryProviderAdapter(string? clusterName)
        {
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

            return new ClusterRegistryKey(currentKey);
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

    /// <summary>
    /// Cluster registry key implementation.
    /// </summary>
    internal class ClusterRegistryKey : IRegistryKey
    {
        private readonly SafeClusterKeyHandle keyHandle;
        private bool disposed;

        public ClusterRegistryKey(SafeClusterKeyHandle keyHandle)
        {
            this.keyHandle = keyHandle ?? throw new ArgumentNullException(nameof(keyHandle));
        }

        public string? GetStringValue(string valueName)
        {
            this.ThrowIfDisposed();
            return this.keyHandle.GetStringValue(valueName);
        }

        public void SetStringValue(string valueName, string value)
        {
            this.ThrowIfDisposed();
            this.keyHandle.SetStringValue(valueName, value);
        }

        public void DeleteValue(string valueName)
        {
            this.ThrowIfDisposed();
            this.keyHandle.DeleteValue(valueName);
        }

        public IList<string> EnumerateValueNames()
        {
            this.ThrowIfDisposed();
            return this.keyHandle.EnumerateValueNames().ToList();
        }

        public void ClearValues()
        {
            this.ThrowIfDisposed();
            this.keyHandle.ClearValues();
        }

        public IRegistryKey CreateOrOpenSubKey(string subKeyName)
        {
            this.ThrowIfDisposed();
            var subKey = this.keyHandle.CreateOrOpenSubKey(subKeyName);
            return new ClusterRegistryKey(subKey);
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(ClusterRegistryKey));
            }
        }

        public void Dispose()
        {
            if (!this.disposed)
            {
                this.keyHandle?.Dispose();
                this.disposed = true;
            }
        }
    }
}