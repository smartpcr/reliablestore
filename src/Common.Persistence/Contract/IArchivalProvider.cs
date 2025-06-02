//-------------------------------------------------------------------------------
// <copyright file="IArchivalProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Contract
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Archival and purge operations for a storage provider.
    /// </summary>
    public interface IArchivalProvider<T> where T : IEntity
    {
        /// <summary>
        /// Archive the entity at the given key, following the configured archive policy.
        /// </summary>
        /// <param name="key">The entity key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task ArchiveAsync(string key, CancellationToken cancellationToken = default);


        /// <summary>
        /// Gets the archive policy configuration for a specific entity type.
        /// </summary>
        /// <param name="entityType">The entity type.</param>
        ArchivePolicy GetArchivePolicy(string entityType);

    }
}
