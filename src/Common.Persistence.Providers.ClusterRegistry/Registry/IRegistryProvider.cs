//-------------------------------------------------------------------------------
// <copyright file="IRegistryProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Registry
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.Versioning;

    /// <summary>
    /// Abstraction for registry operations that can work with both cluster registry and local Windows registry.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal interface IRegistryProvider : IDisposable
    {
        /// <summary>
        /// Gets or creates a registry key at the specified path.
        /// </summary>
        IRegistryKey GetOrCreateKey(string keyPath);

        /// <summary>
        /// Gets a value indicating whether this is a cluster registry provider.
        /// </summary>
        bool IsCluster { get; }
    }

    /// <summary>
    /// Abstraction for a registry key.
    /// </summary>
    internal interface IRegistryKey : IDisposable
    {
        /// <summary>
        /// Gets a string value from the registry.
        /// </summary>
        string? GetStringValue(string valueName);

        /// <summary>
        /// Sets a string value in the registry.
        /// </summary>
        void SetStringValue(string valueName, string value);

        /// <summary>
        /// Deletes a value from the registry.
        /// </summary>
        void DeleteValue(string valueName);

        /// <summary>
        /// Enumerates all value names in the registry key.
        /// </summary>
        IList<string> EnumerateValueNames();

        /// <summary>
        /// Clears all values in the registry key.
        /// </summary>
        void ClearValues();

        /// <summary>
        /// Creates or opens a subkey.
        /// </summary>
        IRegistryKey CreateOrOpenSubKey(string subKeyName);
    }
}