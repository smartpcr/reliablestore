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
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Cluster registry key implementation.
    /// </summary>
    internal class ClusterRegistryKey : IRegistryKey
    {
        private readonly SafeClusterKeyHandle keyHandle;
        private readonly ILogger? logger;
        private readonly string keyPath;
        private bool disposed;

        public ClusterRegistryKey(SafeClusterKeyHandle keyHandle, string keyPath = "", ILogger? logger = null)
        {
            this.keyHandle = keyHandle ?? throw new ArgumentNullException(nameof(keyHandle));
            this.keyPath = keyPath;
            this.logger = logger;
        }

        public string? GetStringValue(string valueName)
        {
            this.ThrowIfDisposed();
            return this.keyHandle.GetStringValue(valueName);
        }

        public void SetStringValue(string valueName, string value)
        {
            this.ThrowIfDisposed();
            
            if (this.logger?.IsEnabled(LogLevel.Debug) == true)
            {
                var valueLength = value?.Length ?? 0;
                this.logger.LogDebug("Setting string value '{ValueName}' at key path '{KeyPath}', value length: {ValueLength} characters",
                    valueName, this.keyPath, valueLength);
            }
            
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
            
            if (this.logger?.IsEnabled(LogLevel.Debug) == true)
            {
                var valueLength = value?.Length ?? 0;
                this.logger.LogDebug("Setting binary value '{ValueName}' at key path '{KeyPath}', value length: {ValueLength} bytes",
                    valueName, this.keyPath, valueLength);
            }
            
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
            var subKeyPath = string.IsNullOrEmpty(this.keyPath) ? subKeyName : $"{this.keyPath}\\{subKeyName}";
            return new ClusterRegistryKey(subKey, subKeyPath, this.logger);
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