//-------------------------------------------------------------------------------
// <copyright file="IEntity.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Contract
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Contract for an entity that can be stored in a provider-backed store.
    /// </summary>
    public interface IEntity
    {
        /// <summary>
        /// Gets the unique key for this entity.
        /// </summary>
        string Key { get; }

        /// <summary>
        /// Gets the version of this entity. Default is 1.
        /// </summary>
        long Version { get; }

        /// <summary>
        /// Gets the ETag for this entity (for concurrency control, optional).
        /// </summary>
        string? ETag { get; }

        /// <summary>
        /// Gets the optional index fields for this entity (for querying and indexing).
        /// </summary>
        IReadOnlyDictionary<string, object>? IndexFields { get; }

        /// <summary>
        /// Gets the list of webhook URIs to notify on entity change (optional).
        /// </summary>
        IReadOnlyList<Uri>? ChangeSubscriptions { get; }

        /// <summary>
        /// Gets the optional checkout date for this entity (e.g., for lease or reservation scenarios).
        /// </summary>
        DateTimeOffset? CheckoutDate { get; }

        /// <summary>
        /// Gets the optional checkout expiry for this entity (e.g., for lease or reservation scenarios).
        /// </summary>
        DateTimeOffset? CheckoutExpiry { get; }
    }
}

