//-------------------------------------------------------------------------------
// <copyright file="ClusterApiInterop.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Api
{
    using System;
    using System.Runtime.InteropServices;
    using System.Security.AccessControl;
    using System.Text;

    /// <summary>
    /// P/Invoke declarations for Windows Cluster API.
    /// </summary>
    internal static class ClusterApiInterop
    {
        // Cluster Management APIs
        [DllImport("clusapi.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern IntPtr OpenCluster(string? clusterName);

        [DllImport("clusapi.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern IntPtr OpenClusterEx(string? clusterName, ClusterAccessRights desiredAccess, out ClusterAccessRights grantedAccess);

        [DllImport("clusapi.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseCluster(IntPtr clusterHandle);

        // Registry Key Management
        [DllImport("clusapi.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern IntPtr GetClusterKey(SafeClusterHandle clusterHandle, RegistryRights rights);

        [DllImport("clusapi.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int ClusterRegOpenKey(SafeClusterKeyHandle clusterKeyHandle, string subKey, RegistryRights rights, out IntPtr subKeyHandle);

        [DllImport("clusapi.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int ClusterRegCreateKey(SafeClusterKeyHandle clusterKeyHandle, string subKey, int options, RegistryRights rights, IntPtr securityAttributes, out IntPtr subKeyHandle, out int disposition);

        [DllImport("clusapi.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int ClusterRegDeleteKey(SafeClusterKeyHandle clusterKeyHandle, string subKey);

        [DllImport("clusapi.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int ClusterRegDeleteValue(SafeClusterKeyHandle key, string valueName);

        [DllImport("clusapi.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int ClusterRegCloseKey(IntPtr key);

        // Value Operations
        [DllImport("clusapi.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int ClusterRegSetValue(SafeClusterKeyHandle key, string valueName, ClusterRegistryValueType valueType, IntPtr data, int dataSize);

        [DllImport("clusapi.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int ClusterRegQueryValue(SafeClusterKeyHandle key, string valueName, out ClusterRegistryValueType valueType, IntPtr data, ref int dataSize);

        // Enumeration APIs
        [DllImport("clusapi.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int ClusterRegEnumKey(SafeClusterKeyHandle clusterKeyHandle, int index, StringBuilder keyName, ref int keyNameSize, out IntPtr lastWriteTime);

        [DllImport("clusapi.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int ClusterRegEnumValue(SafeClusterKeyHandle clusterKeyHandle, int index, StringBuilder valueName, ref int valueNameSize, out ClusterRegistryValueType valueType, IntPtr data, ref int dataSize);

        // Batch Operations
        [DllImport("clusapi.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int ClusterRegCreateBatch(SafeClusterKeyHandle key, out SafeClusterBatchHandle registryBatch);

        [DllImport("clusapi.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int ClusterRegBatchAddCommand(SafeClusterBatchHandle registryBatch, ClusterRegCommand command, string subKeyName, ClusterRegistryValueType type, IntPtr data, int dataLength);

        [DllImport("clusapi.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int ClusterRegCloseBatch(SafeClusterBatchHandle registryBatch, bool commit, ref int failedCommandIndex);

        // Notification APIs (for future use)
        [DllImport("clusapi.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int ClusterRegCreateBatchNotifyPort(SafeClusterKeyHandle key, out SafeClusterNotifyHandle registryNotifyPort);

        [DllImport("clusapi.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int ClusterRegCloseBatchNotifyPort(SafeClusterNotifyHandle notificationHandle);

    }
}