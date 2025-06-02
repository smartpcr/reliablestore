//-------------------------------------------------------------------------------
// <copyright file="ProviderHealth.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Contract
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Represents health and statistics for a storage provider, grouped by entity type.
    /// </summary>
    public sealed class ProviderHealth
    {
        /// <summary>
        /// Gets or sets the overall health status.
        /// </summary>
        public bool IsHealthy { get; set; }

        /// <summary>
        /// Gets or sets an optional health message.
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Gets or sets an optional exception if the provider is unhealthy.
        /// </summary>
        public Exception? Exception { get; set; }

        /// <summary>
        /// Gets or sets the per-entity-type statistics.
        /// </summary>
        public Dictionary<string, ProviderTypeStats> TypeStats { get; set; } = new();
    }

    /// <summary>
    /// Statistics for a specific entity type in the provider.
    /// </summary>
    public sealed class ProviderTypeStats
    {
        /// <summary>
        /// Gets or sets the total number of store operations.
        /// </summary>
        public long StoreCount { get; set; }

        /// <summary>
        /// Gets or sets the total number of archive operations.
        /// </summary>
        public long ArchiveCount { get; set; }

        /// <summary>
        /// Gets or sets the total number of purge operations.
        /// </summary>
        public long PurgeCount { get; set; }

        /// <summary>
        /// Gets or sets the total number of calls (all operations).
        /// </summary>
        public long CallCount { get; set; }

        /// <summary>
        /// Gets or sets the average latency in milliseconds.
        /// </summary>
        public double AverageLatencyMs { get; set; }

        /// <summary>
        /// Gets or sets the total size of stored entities (bytes).
        /// </summary>
        public long StoreSizeBytes { get; set; }

        /// <summary>
        /// Gets or sets the total size of archived entities (bytes).
        /// </summary>
        public long ArchiveSizeBytes { get; set; }

        /// <summary>
        /// Gets or sets the total size of purged entities (bytes).
        /// </summary>
        public long PurgedSizeBytes { get; set; }
    }
}

