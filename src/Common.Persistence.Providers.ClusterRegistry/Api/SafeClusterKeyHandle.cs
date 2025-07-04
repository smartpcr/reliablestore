// -----------------------------------------------------------------------
// <copyright file="SafeClusterKeyHandle.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Api
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Runtime.InteropServices;
    using System.Runtime.Versioning;
    using System.Security.AccessControl;
    using System.Text;
    using Common.Persistence.Providers.ClusterRegistry.Core;

    /// <summary>
    /// Safe handle wrapper for cluster registry key handles.
    /// </summary>
    internal sealed class SafeClusterKeyHandle : SafeHandle
    {
        private SafeClusterKeyHandle() : base(IntPtr.Zero, true)
        {
        }

        internal SafeClusterKeyHandle(IntPtr handle) : base(IntPtr.Zero, true)
        {
            this.SetHandle(handle);
        }

        /// <summary>
        /// Gets a value indicating whether the handle is invalid.
        /// </summary>
        public override bool IsInvalid => this.handle == IntPtr.Zero;

        /// <summary>
        /// Creates or opens a subkey.
        /// </summary>
        /// <param name="subKeyName">The name of the subkey.</param>
        /// <returns>A safe cluster key handle for the subkey.</returns>
        /// <exception cref="ClusterPersistenceException">Thrown when the operation fails.</exception>
#if NET5_0_OR_GREATER
        [SupportedOSPlatform("windows")]
#endif
        public SafeClusterKeyHandle CreateOrOpenSubKey(string subKeyName)
        {
            if (string.IsNullOrEmpty(subKeyName))
            {
                throw new ArgumentException("Subkey name cannot be null or empty.", nameof(subKeyName));
            }

            var result = ClusterApiInterop.ClusterRegCreateKey(
                this,
                subKeyName,
                0, // options
                RegistryRights.FullControl,
                IntPtr.Zero, // security attributes
                out var subKeyHandle,
                out var disposition);

            if (result != (int)ClusterErrorCode.Success)
            {
                var error = result;
                throw new ClusterPersistenceException($"Failed to create or open subkey '{subKeyName}'. Error: {error}", new Win32Exception(error));
            }

            return new SafeClusterKeyHandle(subKeyHandle);
        }

        /// <summary>
        /// Opens an existing subkey.
        /// </summary>
        /// <param name="subKeyName">The name of the subkey.</param>
        /// <returns>A safe cluster key handle for the subkey, or null if the key doesn't exist.</returns>
#if NET5_0_OR_GREATER
        [SupportedOSPlatform("windows")]
#endif
        public SafeClusterKeyHandle? OpenSubKey(string subKeyName)
        {
            if (string.IsNullOrEmpty(subKeyName))
            {
                throw new ArgumentException("Subkey name cannot be null or empty.", nameof(subKeyName));
            }

            var result = ClusterApiInterop.ClusterRegOpenKey(
                this,
                subKeyName,
                RegistryRights.FullControl,
                out var subKeyHandle);

            if (result == (int)ClusterErrorCode.FileNotFound)
            {
                return null; // Key doesn't exist
            }

            if (result != (int)ClusterErrorCode.Success)
            {
                var error = result;
                throw new ClusterPersistenceException($"Failed to open subkey '{subKeyName}'. Error: {error}", new Win32Exception(error));
            }

            return new SafeClusterKeyHandle(subKeyHandle);
        }

        /// <summary>
        /// Deletes a subkey and all its values.
        /// </summary>
        /// <param name="subKeyName">The name of the subkey to delete.</param>
        /// <returns>True if the key was deleted, false if it didn't exist.</returns>
        /// <exception cref="ClusterPersistenceException">Thrown when the operation fails.</exception>
        public bool DeleteSubKey(string subKeyName)
        {
            if (string.IsNullOrEmpty(subKeyName))
            {
                throw new ArgumentException("Subkey name cannot be null or empty.", nameof(subKeyName));
            }

            var result = ClusterApiInterop.ClusterRegDeleteKey(this, subKeyName);

            if (result == (int)ClusterErrorCode.FileNotFound)
            {
                return false; // Key didn't exist
            }

            if (result != (int)ClusterErrorCode.Success)
            {
                var error = result;
                throw new ClusterPersistenceException($"Failed to delete subkey '{subKeyName}'. Error: {error}", new Win32Exception(error));
            }

            return true;
        }

        /// <summary>
        /// Sets a string value in the registry.
        /// </summary>
        /// <param name="valueName">The name of the value.</param>
        /// <param name="value">The string value to set.</param>
        /// <exception cref="ClusterPersistenceException">Thrown when the operation fails.</exception>
        public void SetStringValue(string valueName, string value)
        {
            if (valueName == null)
            {
                throw new ArgumentNullException(nameof(valueName));
            }

            var data = IntPtr.Zero;
            try
            {
                var byteCount = (value?.Length ?? 0 + 1) * sizeof(char); // +1 for null terminator
                data = Marshal.StringToHGlobalUni(value);

                var result = ClusterApiInterop.ClusterRegSetValue(
                    this,
                    valueName,
                    ClusterRegistryValueType.String,
                    data,
                    byteCount);

                if (result != (int)ClusterErrorCode.Success)
                {
                    var error = result;
                    throw new ClusterPersistenceException($"Failed to set value '{valueName}'. Error: {error}", new Win32Exception(error));
                }
            }
            finally
            {
                if (data != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(data);
                }
            }
        }

        /// <summary>
        /// Gets a string value from the registry.
        /// </summary>
        /// <param name="valueName">The name of the value.</param>
        /// <returns>The string value, or null if the value doesn't exist.</returns>
        /// <exception cref="ClusterPersistenceException">Thrown when the operation fails.</exception>
        public string? GetStringValue(string valueName)
        {
            if (valueName == null)
            {
                throw new ArgumentNullException(nameof(valueName));
            }

            var dataSize = 0;

            // First call to get the required buffer size
            var result = ClusterApiInterop.ClusterRegQueryValue(
                this,
                valueName,
                out var valueType,
                IntPtr.Zero,
                ref dataSize);

            if (result == (int)ClusterErrorCode.FileNotFound)
            {
                return null; // Value doesn't exist
            }

            if (result != (int)ClusterErrorCode.MoreData && result != (int)ClusterErrorCode.Success)
            {
                var error = result;
                throw new ClusterPersistenceException($"Failed to query value '{valueName}'. Error: {error}", new Win32Exception(error));
            }

            if (dataSize == 0)
            {
                return string.Empty;
            }

            // Second call to get the actual data
            var data = Marshal.AllocHGlobal(dataSize);
            try
            {
                result = ClusterApiInterop.ClusterRegQueryValue(
                    this,
                    valueName,
                    out valueType,
                    data,
                    ref dataSize);

                if (result != (int)ClusterErrorCode.Success)
                {
                    var error = result;
                    throw new ClusterPersistenceException($"Failed to get value '{valueName}'. Error: {error}", new Win32Exception(error));
                }

                return Marshal.PtrToStringUni(data, dataSize / sizeof(char) - 1); // -1 to exclude null terminator
            }
            finally
            {
                Marshal.FreeHGlobal(data);
            }
        }

        /// <summary>
        /// Sets a binary value in the registry.
        /// </summary>
        /// <param name="valueName">The name of the value.</param>
        /// <param name="value">The binary value to set.</param>
        /// <exception cref="ClusterPersistenceException">Thrown when the operation fails.</exception>
        public void SetBinaryValue(string valueName, byte[] value)
        {
            if (valueName == null)
            {
                throw new ArgumentNullException(nameof(valueName));
            }

            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var data = IntPtr.Zero;
            try
            {
                data = Marshal.AllocHGlobal(value.Length);
                Marshal.Copy(value, 0, data, value.Length);

                var result = ClusterApiInterop.ClusterRegSetValue(
                    this,
                    valueName,
                    ClusterRegistryValueType.Binary,
                    data,
                    value.Length);

                if (result != (int)ClusterErrorCode.Success)
                {
                    var error = result;
                    throw new ClusterPersistenceException($"Failed to set binary value '{valueName}'. Error: {error}", new Win32Exception(error));
                }
            }
            finally
            {
                if (data != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(data);
                }
            }
        }

        /// <summary>
        /// Gets a binary value from the registry.
        /// </summary>
        /// <param name="valueName">The name of the value.</param>
        /// <returns>The binary value, or null if the value doesn't exist.</returns>
        /// <exception cref="ClusterPersistenceException">Thrown when the operation fails.</exception>
        public byte[]? GetBinaryValue(string valueName)
        {
            if (valueName == null)
            {
                throw new ArgumentNullException(nameof(valueName));
            }

            var dataSize = 0;

            // First call to get the required buffer size
            var result = ClusterApiInterop.ClusterRegQueryValue(
                this,
                valueName,
                out var valueType,
                IntPtr.Zero,
                ref dataSize);

            if (result == (int)ClusterErrorCode.FileNotFound)
            {
                return null; // Value doesn't exist
            }

            if (result != (int)ClusterErrorCode.MoreData && result != (int)ClusterErrorCode.Success)
            {
                var error = result;
                throw new ClusterPersistenceException($"Failed to query binary value '{valueName}'. Error: {error}", new Win32Exception(error));
            }

            if (dataSize == 0)
            {
                return Array.Empty<byte>();
            }

            // Second call to get the actual data
            var data = Marshal.AllocHGlobal(dataSize);
            try
            {
                result = ClusterApiInterop.ClusterRegQueryValue(
                    this,
                    valueName,
                    out valueType,
                    data,
                    ref dataSize);

                if (result != (int)ClusterErrorCode.Success)
                {
                    var error = result;
                    throw new ClusterPersistenceException($"Failed to get binary value '{valueName}'. Error: {error}", new Win32Exception(error));
                }

                var bytes = new byte[dataSize];
                Marshal.Copy(data, bytes, 0, dataSize);
                return bytes;
            }
            finally
            {
                Marshal.FreeHGlobal(data);
            }
        }

        /// <summary>
        /// Deletes a value from the registry.
        /// </summary>
        /// <param name="valueName">The name of the value to delete.</param>
        /// <returns>True if the value was deleted, false if it didn't exist.</returns>
        /// <exception cref="ClusterPersistenceException">Thrown when the operation fails.</exception>
        public bool DeleteValue(string valueName)
        {
            if (valueName == null)
            {
                throw new ArgumentNullException(nameof(valueName));
            }

            var result = ClusterApiInterop.ClusterRegDeleteValue(this, valueName);

            if (result == (int)ClusterErrorCode.FileNotFound)
            {
                return false; // Value didn't exist
            }

            if (result != (int)ClusterErrorCode.Success)
            {
                var error = result;
                throw new ClusterPersistenceException($"Failed to delete value '{valueName}'. Error: {error}", new Win32Exception(error));
            }

            return true;
        }

        /// <summary>
        /// Enumerates all value names in this key.
        /// </summary>
        /// <returns>A list of value names.</returns>
        /// <exception cref="ClusterPersistenceException">Thrown when enumeration fails.</exception>
        public IList<string> EnumerateValueNames()
        {
            var valueNames = new List<string>();
            var index = 0;

            while (true)
            {
                var nameSize = 256; // Initial buffer size
                var name = new StringBuilder(nameSize);

                var result = ClusterApiInterop.ClusterRegEnumValue(
                    this,
                    index,
                    name,
                    ref nameSize,
                    out var valueType,
                    IntPtr.Zero,
                    ref nameSize);

                if (result == (int)ClusterErrorCode.NoMoreItems)
                {
                    break; // Enumeration complete
                }

                if (result == (int)ClusterErrorCode.MoreData)
                {
                    // Retry with larger buffer
                    name = new StringBuilder(nameSize + 1);
                    result = ClusterApiInterop.ClusterRegEnumValue(
                        this,
                        index,
                        name,
                        ref nameSize,
                        out valueType,
                        IntPtr.Zero,
                        ref nameSize);
                }

                if (result != (int)ClusterErrorCode.Success)
                {
                    var error = result;
                    throw new ClusterPersistenceException($"Failed to enumerate values at index {index}. Error: {error}", new Win32Exception(error));
                }

                valueNames.Add(name.ToString());
                index++;
            }

            return valueNames;
        }

        /// <summary>
        /// Enumerates all subkey names in this key.
        /// </summary>
        /// <returns>A list of subkey names.</returns>
        /// <exception cref="ClusterPersistenceException">Thrown when enumeration fails.</exception>
        public IList<string> EnumerateSubKeyNames()
        {
            var subKeyNames = new List<string>();
            var index = 0;

            while (true)
            {
                var nameSize = 256; // Initial buffer size
                var name = new StringBuilder(nameSize);

                var result = ClusterApiInterop.ClusterRegEnumKey(
                    this,
                    index,
                    name,
                    ref nameSize,
                    out var lastWriteTime);

                if (result == (int)ClusterErrorCode.NoMoreItems)
                {
                    break; // Enumeration complete
                }

                if (result == (int)ClusterErrorCode.MoreData)
                {
                    // Retry with larger buffer
                    name = new StringBuilder(nameSize + 1);
                    result = ClusterApiInterop.ClusterRegEnumKey(
                        this,
                        index,
                        name,
                        ref nameSize,
                        out lastWriteTime);
                }

                if (result != (int)ClusterErrorCode.Success)
                {
                    var error = result;
                    throw new ClusterPersistenceException($"Failed to enumerate subkeys at index {index}. Error: {error}", new Win32Exception(error));
                }

                subKeyNames.Add(name.ToString());
                index++;
            }

            return subKeyNames;
        }

        /// <summary>
        /// Clears all values in this key.
        /// </summary>
        /// <exception cref="ClusterPersistenceException">Thrown when the operation fails.</exception>
        public void ClearValues()
        {
            var valueNames = this.EnumerateValueNames();
            foreach (var valueName in valueNames)
            {
                this.DeleteValue(valueName);
            }
        }

        /// <summary>
        /// Releases the cluster key handle.
        /// </summary>
        /// <returns>True if the handle was released successfully.</returns>
        protected override bool ReleaseHandle()
        {
            if (this.handle != IntPtr.Zero)
            {
                var result = ClusterApiInterop.ClusterRegCloseKey(this.handle);
                return result == (int)ClusterErrorCode.Success;
            }
            return true;
        }
    }
}