//-------------------------------------------------------------------------------
// <copyright file="SafeClusterHandle.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Api
{
    using System;
    using System.ComponentModel;
    using System.Runtime.InteropServices;
    using System.Runtime.Versioning;
    using System.Security.AccessControl;
    using Common.Persistence.Providers.ClusterRegistry.Core;
    using Microsoft.Win32.SafeHandles;

    /// <summary>
    /// Safe handle wrapper for cluster handles.
    /// </summary>
    internal sealed class SafeClusterHandle : SafeHandle
    {
        private SafeClusterHandle() : base(IntPtr.Zero, true)
        {
        }

        private SafeClusterHandle(IntPtr handle) : base(IntPtr.Zero, true)
        {
            this.SetHandle(handle);
        }

        /// <summary>
        /// Gets a value indicating whether the handle is invalid.
        /// </summary>
        public override bool IsInvalid => this.handle == IntPtr.Zero;

        /// <summary>
        /// Opens a handle to the specified cluster.
        /// </summary>
        /// <param name="clusterName">The cluster name (null for local cluster).</param>
        /// <returns>A safe cluster handle.</returns>
        /// <exception cref="ClusterConnectionException">Thrown when cluster connection fails.</exception>
        public static SafeClusterHandle Open(string? clusterName = null)
        {
            var desiredAccess = ClusterAccessRights.MaximumAllowed;
            var clusterHandle = ClusterApiInterop.OpenClusterEx(clusterName, desiredAccess, out var grantedAccess);

            if (clusterHandle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                var errorMessage = GetClusterErrorMessage(error, clusterName);
                throw new ClusterConnectionException(errorMessage, new Win32Exception(error));
            }

            return new SafeClusterHandle(clusterHandle);
        }

        /// <summary>
        /// Gets the root registry key for this cluster.
        /// </summary>
        /// <returns>A safe cluster key handle for the root registry key.</returns>
        /// <exception cref="ClusterPersistenceException">Thrown when getting the root key fails.</exception>
#if NET5_0_OR_GREATER
        [SupportedOSPlatform("windows")]
#endif
        public SafeClusterKeyHandle GetRootKey()
        {
            if (this.IsInvalid)
            {
                throw new ClusterPersistenceException("Cannot get root key from invalid cluster handle.");
            }

            var rootKeyHandle = ClusterApiInterop.GetClusterKey(this, RegistryRights.FullControl);
            if (rootKeyHandle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                throw new ClusterPersistenceException($"Failed to get cluster root key. Error: {error}", new Win32Exception(error));
            }

            return new SafeClusterKeyHandle(rootKeyHandle);
        }

        /// <summary>
        /// Releases the cluster handle.
        /// </summary>
        /// <returns>True if the handle was released successfully.</returns>
        protected override bool ReleaseHandle()
        {
            if (this.handle != IntPtr.Zero)
            {
                return ClusterApiInterop.CloseCluster(this.handle);
            }
            return true;
        }

        private static string GetClusterErrorMessage(int errorCode, string? clusterName)
        {
            var clusterDisplayName = string.IsNullOrEmpty(clusterName) ? "local cluster" : $"cluster '{clusterName}'";

            return errorCode switch
            {
                (int)ClusterErrorCode.AccessDenied => $"Access denied when connecting to {clusterDisplayName}. Ensure the current user has cluster access rights.",
                (int)ClusterErrorCode.ClusterNodeDown => $"The {clusterDisplayName} is not available or the cluster service is not running.",
                (int)ClusterErrorCode.NodeNotAvailable => $"The cluster node is not available. The cluster service may not be running.",
                (int)ClusterErrorCode.ClusterNoQuorum => $"The {clusterDisplayName} does not have quorum.",
                (int)ClusterErrorCode.FileNotFound => $"The {clusterDisplayName} was not found.",
                (int)ClusterErrorCode.HostNotFound => $"The host for {clusterDisplayName} could not be found. Check the cluster name and network connectivity.",
                _ => $"Failed to connect to {clusterDisplayName}. Error code: {errorCode}"
            };
        }
    }
}