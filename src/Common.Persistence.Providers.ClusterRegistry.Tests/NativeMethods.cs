//-------------------------------------------------------------------------------
// <copyright file="NativeMethods.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Tests
{
    using System;
    using System.Runtime.InteropServices;
    using Microsoft.Win32.SafeHandles;

    internal static class NativeMethods
    {
        internal const uint SC_MANAGER_CONNECT = 0x0001;
        internal const uint SERVICE_QUERY_STATUS = 0x0004;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern ServiceControlHandle OpenSCManager(
            string? lpMachineName,
            string? lpDatabaseName,
            uint dwDesiredAccess);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern ServiceControlHandle OpenService(
            ServiceControlHandle hSCManager,
            string lpServiceName,
            uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool CloseServiceHandle(IntPtr hSCObject);
    }

    internal class ServiceControlHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ServiceControlHandle() : base(true) { }

        protected override bool ReleaseHandle()
        {
            return NativeMethods.CloseServiceHandle(this.handle);
        }
    }
}