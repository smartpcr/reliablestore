// -----------------------------------------------------------------------
// <copyright file="IRegistryKey.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Registry
{
    using System;
    using System.Collections.Generic;

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
        /// Gets a binary value from the registry.
        /// </summary>
        byte[]? GetBinaryValue(string valueName);

        /// <summary>
        /// Sets a binary value in the registry.
        /// </summary>
        void SetBinaryValue(string valueName, byte[] value);

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