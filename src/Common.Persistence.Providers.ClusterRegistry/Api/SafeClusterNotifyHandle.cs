// -----------------------------------------------------------------------
// <copyright file="SafeClusterNotifyHandle.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Api
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Safe handle wrapper for cluster notification handles.
    /// </summary>
    internal sealed class SafeClusterNotifyHandle : SafeHandle
    {
        private SafeClusterNotifyHandle() : base(IntPtr.Zero, true)
        {
        }

        internal SafeClusterNotifyHandle(IntPtr handle) : base(IntPtr.Zero, true)
        {
            this.SetHandle(handle);
        }

        /// <summary>
        /// Gets a value indicating whether the handle is invalid.
        /// </summary>
        public override bool IsInvalid => this.handle == IntPtr.Zero;

        /// <summary>
        /// Releases the notification handle.
        /// </summary>
        /// <returns>True if the handle was released successfully.</returns>
        protected override bool ReleaseHandle()
        {
            if (this.handle != IntPtr.Zero)
            {
                var result = ClusterApiInterop.ClusterRegCloseBatchNotifyPort(this);
                return result == (int)ClusterErrorCode.Success;
            }
            return true;
        }
    }
}