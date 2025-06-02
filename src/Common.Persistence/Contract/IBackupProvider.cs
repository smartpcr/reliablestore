//-------------------------------------------------------------------------------
// <copyright file="IBackupProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Contract
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Backup operations for a storage provider. Backup is performed as a scheduled job, not by trigger.
    /// </summary>
    public interface IBackupProvider<T> where T : IEntity
    {
        /// <summary>
        /// Runs a backup for all entities of the given type, following the configured backup policy.
        /// </summary>
        /// <param name="entityType">The entity type to back up.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task BackupAsync(string entityType, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the backup policy configuration for a specific entity type.
        /// </summary>
        /// <param name="entityType">The entity type.</param>
        BackupPolicy<T> GetBackupPolicy(string entityType);
    }
}

