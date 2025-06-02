// -----------------------------------------------------------------------
// <copyright file="IPurgeProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Contract
{
    using System.Threading;
    using System.Threading.Tasks;

    public interface IPurgeProvider<T> where T : IEntity
    {
        /// <summary>
        /// Purge all entities of the given type, following the configured purge policy. This operation may be automatically triggered based on policy (e.g., store size) and is often called after archive.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task PurgeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the purge policy configuration for a specific entity type.
        /// </summary>
        /// <param name="entityType">The entity type.</param>
        PurgePolicy<T> GetPurgePolicy(string entityType);
    }
}