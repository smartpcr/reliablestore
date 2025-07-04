//-------------------------------------------------------------------------------
// <copyright file="IRegistryProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Registry
{
    using System;
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
}