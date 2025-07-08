//-------------------------------------------------------------------------------
// <copyright file="LocalRegistryProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Registry
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.Versioning;
    using Microsoft.Extensions.Logging;
    using Microsoft.Win32;

    /// <summary>
    /// Registry provider that uses the local Windows Registry.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal class LocalRegistryProvider : IRegistryProvider
    {
        private readonly RegistryKey rootKey;
        private readonly string rootPath;
        private readonly ILogger? logger;
        private bool disposed;

        public LocalRegistryProvider(string rootPath, ILogger? logger = null)
        {
            this.rootPath = rootPath;
            this.logger = logger;
            // Use HKEY_LOCAL_MACHINE for local registry
            this.rootKey = Registry.LocalMachine.CreateSubKey(rootPath, true);
        }

        public bool IsCluster => false;

        public IRegistryKey GetOrCreateKey(string keyPath)
        {
            this.ThrowIfDisposed();
            
            var subKey = this.rootKey.CreateSubKey(keyPath, true);
            if (subKey == null)
            {
                throw new InvalidOperationException($"Failed to create or open registry key: {keyPath}");
            }

            var fullPath = $"{this.rootPath}\\{keyPath}";
            return new LocalRegistryKey(subKey, fullPath, this.logger);
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(LocalRegistryProvider));
            }
        }

        public void Dispose()
        {
            if (!this.disposed)
            {
                this.rootKey?.Dispose();
                this.disposed = true;
            }
        }
    }

    /// <summary>
    /// Local registry key implementation.
    /// </summary>
    internal class LocalRegistryKey : IRegistryKey
    {
        private readonly RegistryKey key;
        private readonly string keyPath;
        private readonly ILogger? logger;
        private bool disposed;

        public LocalRegistryKey(RegistryKey key, string keyPath = "", ILogger? logger = null)
        {
            this.key = key ?? throw new ArgumentNullException(nameof(key));
            this.keyPath = keyPath;
            this.logger = logger;
        }

        public string? GetStringValue(string valueName)
        {
            this.ThrowIfDisposed();
            return this.key.GetValue(valueName) as string;
        }

        public void SetStringValue(string valueName, string value)
        {
            this.ThrowIfDisposed();
            
            if (this.logger?.IsEnabled(LogLevel.Debug) == true)
            {
                var valueLength = value?.Length ?? 0;
                this.logger.LogDebug("Setting string value '{ValueName}' at key path 'HKLM:\\{KeyPath}', value length: {ValueLength} characters",
                    valueName, this.keyPath, valueLength);
            }
            
            this.key.SetValue(valueName, value, RegistryValueKind.String);
        }

        public byte[]? GetBinaryValue(string valueName)
        {
            this.ThrowIfDisposed();
            return this.key.GetValue(valueName) as byte[];
        }

        public void SetBinaryValue(string valueName, byte[] value)
        {
            this.ThrowIfDisposed();
            
            if (this.logger?.IsEnabled(LogLevel.Debug) == true)
            {
                var valueLength = value?.Length ?? 0;
                this.logger.LogDebug("Setting binary value '{ValueName}' at key path 'HKLM:\\{KeyPath}', value length: {ValueLength} bytes",
                    valueName, this.keyPath, valueLength);
            }
            
            this.key.SetValue(valueName, value, RegistryValueKind.Binary);
        }

        public void DeleteValue(string valueName)
        {
            this.ThrowIfDisposed();
            try
            {
                this.key.DeleteValue(valueName);
            }
            catch (ArgumentException)
            {
                // Value doesn't exist, ignore
            }
        }

        public IList<string> EnumerateValueNames()
        {
            this.ThrowIfDisposed();
            return this.key.GetValueNames();
        }

        public void ClearValues()
        {
            this.ThrowIfDisposed();
            var valueNames = this.key.GetValueNames();
            foreach (var valueName in valueNames)
            {
                this.key.DeleteValue(valueName);
            }
        }

        public IRegistryKey CreateOrOpenSubKey(string subKeyName)
        {
            this.ThrowIfDisposed();
            var subKey = this.key.CreateSubKey(subKeyName, true);
            if (subKey == null)
            {
                throw new InvalidOperationException($"Failed to create or open subkey: {subKeyName}");
            }
            var subKeyPath = string.IsNullOrEmpty(this.keyPath) ? subKeyName : $"{this.keyPath}\\{subKeyName}";
            return new LocalRegistryKey(subKey, subKeyPath, this.logger);
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(LocalRegistryKey));
            }
        }

        public void Dispose()
        {
            if (!this.disposed)
            {
                this.key?.Dispose();
                this.disposed = true;
            }
        }
    }
}