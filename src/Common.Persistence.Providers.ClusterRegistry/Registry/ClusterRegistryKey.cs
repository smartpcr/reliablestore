// -----------------------------------------------------------------------
// <copyright file="ClusterRegistryKey.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Registry
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Common.Persistence.Providers.ClusterRegistry.Api;

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

        public byte[]? GetBinaryValue(string valueName)
        {
            this.ThrowIfDisposed();
            return this.keyHandle.GetBinaryValue(valueName);
        }

        public void SetBinaryValue(string valueName, byte[] value)
        {
            this.ThrowIfDisposed();
            this.keyHandle.SetBinaryValue(valueName, value);
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