//-------------------------------------------------------------------------------
// <copyright file="IMigrationProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Contract
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Contract for a migration provider, responsible for updating entity versions and moving entities between providers.
    /// </summary>
    public interface IMigrationProvider<T> where T : IEntity
    {
        /// <summary>
        /// Migrates an entity to a target version, applying necessary transformations.
        /// </summary>
        /// <param name="key">The entity key.</param>
        /// <param name="targetVersion">The version to migrate to.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task MigrateEntityAsync(string key, long targetVersion, CancellationToken cancellationToken = default);

        /// <summary>
        /// Migrates all entities of a given type to a target version.
        /// </summary>
        /// <param name="targetVersion">The version to migrate to.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task MigrateAllEntitiesAsync(long targetVersion, CancellationToken cancellationToken = default);

        /// <summary>
        /// Moves an entity from one provider to another.
        /// </summary>
        /// <param name="key">The entity key.</param>
        /// <param name="sourceProvider">The source provider name or identifier.</param>
        /// <param name="targetProvider">The target provider name or identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task MoveEntityAsync(string key, string sourceProvider, string targetProvider, CancellationToken cancellationToken = default);

        /// <summary>
        /// Moves all entities of a given type from one provider to another.
        /// </summary>
        /// <param name="sourceProvider">The source provider name or identifier.</param>
        /// <param name="targetProvider">The target provider name or identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task MoveAllEntitiesAsync(string sourceProvider, string targetProvider, CancellationToken cancellationToken = default);
    }
}

