//-------------------------------------------------------------------------------
// <copyright file="ProviderIndex.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Contract
{
    /// <summary>
    /// Represents an index mapping for a storage provider, including query, store type, entity type, and entity path.
    /// </summary>
    public sealed class ProviderIndex
    {
        /// <summary>
        /// Gets or sets the index name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the OData query string that this index supports.
        /// </summary>
        public string Query { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the type of the underlying store (e.g., FileSystem, Sql, InMemory).
        /// </summary>
        public string StoreType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the entity type this index is for.
        /// </summary>
        public string EntityType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the hierarchical path where the entity is stored in the store.
        /// </summary>
        public string EntityPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether the index is unique.
        /// </summary>
        public bool IsUnique { get; set; }
    }
}

