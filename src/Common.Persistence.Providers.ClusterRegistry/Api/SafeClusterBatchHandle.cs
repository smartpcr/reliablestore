// -----------------------------------------------------------------------
// <copyright file="SafeClusterBatchHandle.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Api
{
    using System;
    using System.ComponentModel;
    using System.Runtime.InteropServices;
    using Common.Persistence.Providers.ClusterRegistry.Core;

    /// <summary>
    /// Safe handle wrapper for cluster registry batch operations.
    /// </summary>
    internal sealed class SafeClusterBatchHandle : SafeHandle
    {
        private SafeClusterBatchHandle() : base(IntPtr.Zero, true)
        {
        }

        internal SafeClusterBatchHandle(IntPtr handle) : base(IntPtr.Zero, true)
        {
            this.SetHandle(handle);
        }

        /// <summary>
        /// Gets a value indicating whether the handle is invalid.
        /// </summary>
        public override bool IsInvalid => this.handle == IntPtr.Zero;

        /// <summary>
        /// Creates a new batch operation handle.
        /// </summary>
        /// <param name="keyHandle">The cluster key handle to create the batch for.</param>
        /// <returns>A safe cluster batch handle.</returns>
        /// <exception cref="ClusterPersistenceException">Thrown when batch creation fails.</exception>
        public static SafeClusterBatchHandle Create(SafeClusterKeyHandle keyHandle)
        {
            if (keyHandle == null)
            {
                throw new ArgumentNullException(nameof(keyHandle));
            }

            if (keyHandle.IsInvalid)
            {
                throw new ArgumentException("Key handle is invalid.", nameof(keyHandle));
            }

            var result = ClusterApiInterop.ClusterRegCreateBatch(keyHandle, out var batchHandle);

            if (result != (int)ClusterErrorCode.Success)
            {
                var error = result;
                throw new ClusterPersistenceException($"Failed to create batch operation. Error: {error}", new Win32Exception(error));
            }

            return new SafeClusterBatchHandle(batchHandle.DangerousGetHandle());
        }

        /// <summary>
        /// Adds a set value command to the batch.
        /// </summary>
        /// <param name="keyName">The key name (can be a subkey path).</param>
        /// <param name="valueName">The value name.</param>
        /// <param name="value">The string value to set.</param>
        /// <exception cref="ClusterPersistenceException">Thrown when adding the command fails.</exception>
        public void AddSetValueCommand(string keyName, string valueName, string value)
        {
            if (keyName == null)
            {
                throw new ArgumentNullException(nameof(keyName));
            }

            if (valueName == null)
            {
                throw new ArgumentNullException(nameof(valueName));
            }

            var data = IntPtr.Zero;
            try
            {
                var byteCount = (value?.Length ?? 0 + 1) * sizeof(char); // +1 for null terminator
                data = Marshal.StringToHGlobalUni(value);

                // First add a create key command to ensure the key exists
                var createResult = ClusterApiInterop.ClusterRegBatchAddCommand(
                    this,
                    ClusterRegCommand.CreateKey,
                    keyName,
                    ClusterRegistryValueType.None,
                    IntPtr.Zero,
                    0);

                if (createResult != (int)ClusterErrorCode.Success)
                {
                    var error = createResult;
                    throw new ClusterPersistenceException($"Failed to add create key command for '{keyName}'. Error: {error}", new Win32Exception(error));
                }

                // Then add the set value command
                var setResult = ClusterApiInterop.ClusterRegBatchAddCommand(
                    this,
                    ClusterRegCommand.SetValue,
                    $"{keyName}\\{valueName}",
                    ClusterRegistryValueType.String,
                    data,
                    byteCount);

                if (setResult != (int)ClusterErrorCode.Success)
                {
                    var error = setResult;
                    throw new ClusterPersistenceException($"Failed to add set value command for '{keyName}\\{valueName}'. Error: {error}", new Win32Exception(error));
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
        /// Adds a delete value command to the batch.
        /// </summary>
        /// <param name="keyName">The key name (can be a subkey path).</param>
        /// <param name="valueName">The value name to delete.</param>
        /// <exception cref="ClusterPersistenceException">Thrown when adding the command fails.</exception>
        public void AddDeleteValueCommand(string keyName, string valueName)
        {
            if (keyName == null)
            {
                throw new ArgumentNullException(nameof(keyName));
            }

            if (valueName == null)
            {
                throw new ArgumentNullException(nameof(valueName));
            }

            var result = ClusterApiInterop.ClusterRegBatchAddCommand(
                this,
                ClusterRegCommand.DeleteValue,
                $"{keyName}\\{valueName}",
                ClusterRegistryValueType.None,
                IntPtr.Zero,
                0);

            if (result != (int)ClusterErrorCode.Success)
            {
                var error = result;
                throw new ClusterPersistenceException($"Failed to add delete value command for '{keyName}\\{valueName}'. Error: {error}", new Win32Exception(error));
            }
        }

        /// <summary>
        /// Adds a delete key command to the batch.
        /// </summary>
        /// <param name="keyName">The key name to delete.</param>
        /// <exception cref="ClusterPersistenceException">Thrown when adding the command fails.</exception>
        public void AddDeleteKeyCommand(string keyName)
        {
            if (keyName == null)
            {
                throw new ArgumentNullException(nameof(keyName));
            }

            var result = ClusterApiInterop.ClusterRegBatchAddCommand(
                this,
                ClusterRegCommand.DeleteKey,
                keyName,
                ClusterRegistryValueType.None,
                IntPtr.Zero,
                0);

            if (result != (int)ClusterErrorCode.Success)
            {
                var error = result;
                throw new ClusterPersistenceException($"Failed to add delete key command for '{keyName}'. Error: {error}", new Win32Exception(error));
            }
        }

        /// <summary>
        /// Commits all commands in the batch atomically.
        /// </summary>
        /// <exception cref="ClusterTransactionException">Thrown when the batch commit fails.</exception>
        public void Commit()
        {
            if (this.IsInvalid)
            {
                throw new ClusterPersistenceException("Cannot commit an invalid batch handle.");
            }

            var failedIndex = -1;
            var result = ClusterApiInterop.ClusterRegCloseBatch(this, true, ref failedIndex);

            if (result != (int)ClusterErrorCode.Success)
            {
                var error = result;
                var message = failedIndex >= 0
                    ? $"Batch commit failed at operation index {failedIndex}. Error: {error}"
                    : $"Batch commit failed. Error: {error}";

                throw new ClusterTransactionException(message, new Win32Exception(error), failedIndex);
            }

            // Mark handle as invalid after successful commit
            this.SetHandleAsInvalid();
        }

        /// <summary>
        /// Rolls back all commands in the batch.
        /// </summary>
        public void Rollback()
        {
            if (this.IsInvalid)
            {
                return; // Already invalid, nothing to rollback
            }

            var failedIndex = -1;
            ClusterApiInterop.ClusterRegCloseBatch(this, false, ref failedIndex);

            // Mark handle as invalid after rollback
            this.SetHandleAsInvalid();
        }

        /// <summary>
        /// Releases the batch handle by rolling back any uncommitted operations.
        /// </summary>
        /// <returns>True if the handle was released successfully.</returns>
        protected override bool ReleaseHandle()
        {
            if (this.handle != IntPtr.Zero)
            {
                var failedIndex = -1;
                var result = ClusterApiInterop.ClusterRegCloseBatch(this, false, ref failedIndex);
                return result == (int)ClusterErrorCode.Success;
            }
            return true;
        }
    }
}