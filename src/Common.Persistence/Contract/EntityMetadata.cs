//-------------------------------------------------------------------------------
// <copyright file="EntityMetadata.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Contract
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Represents metadata for a stored entity, including key, index fields, and timestamp.
    /// </summary>
    public sealed class EntityMetadata<T> where T : IEntity
    {
        /// <summary>
        /// Gets or sets the unique key for the entity.
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the index fields for the entity.
        /// </summary>
        public Dictionary<string, object> IndexFields { get; set; } = new();

        /// <summary>
        /// Gets or sets the last modified or created timestamp for the entity.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets or sets the version of the entity metadata.
        /// </summary>
        public long Version { get; set; } = 1;
    }
}

