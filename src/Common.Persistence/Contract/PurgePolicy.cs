//-------------------------------------------------------------------------------
// <copyright file="PurgePolicy.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Contract
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Configuration for purge policy (trigger, size, etc).
    /// </summary>
    public sealed class PurgePolicy<T> where T : IEntity
    {
        /// <summary>
        /// Gets or sets the trigger for purge (e.g., store size, time interval, custom logic).
        /// </summary>
        public string Trigger { get; set; } = "StoreSize";

        /// <summary>
        /// Gets or sets the maximum store size in bytes before purge is triggered.
        /// </summary>
        public long MaxStoreSizeBytes { get; set; } = 10L << 30; // 10 GB default

        /// <summary>
        /// Gets or sets the minimum interval between purge operations.
        /// </summary>
        public TimeSpan MinPurgeInterval { get; set; } = TimeSpan.FromDays(1);

        /// <summary>
        /// Gets or sets the maximum number of entities to purge in a single operation.
        /// </summary>
        public int MaxEntitiesPerPurge { get; set; } = 10000;

        /// <summary>
        /// Gets or sets whether purge should be triggered automatically after archive.
        /// </summary>
        public bool PurgeAfterArchive { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum age for entities before they are eligible for purge.
        /// </summary>
        public TimeSpan? MaxEntityAge { get; set; }

        /// <summary>
        /// Gets or sets the minimum free space (in bytes) to maintain after purge.
        /// </summary>
        public long? MinFreeSpaceBytes { get; set; }

        /// <summary>
        /// Gets or sets additional custom policy options.
        /// </summary>
        public Dictionary<string, object>? CustomOptions { get; set; }
    }
}

