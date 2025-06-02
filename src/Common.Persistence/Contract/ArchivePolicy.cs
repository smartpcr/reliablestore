//-------------------------------------------------------------------------------
// <copyright file="ArchivePolicy.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Contract
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Configuration for archive policy (frequency, size, compaction, etc).
    /// </summary>
    public sealed class ArchivePolicy
    {
        /// <summary>
        /// How often to archive (e.g., TimeSpan.FromDays(1)).
        /// </summary>
        public TimeSpan ArchiveFrequency { get; set; } = TimeSpan.FromDays(1);

        /// <summary>
        /// Maximum size (in bytes) before triggering archive.
        /// </summary>
        public long MaxArchiveSizeBytes { get; set; } = 1L << 30; // 1 GB default

        /// <summary>
        /// Whether to compact archived data after archiving.
        /// </summary>
        public bool EnableCompaction { get; set; } = true;

        /// <summary>
        /// Additional custom policy options.
        /// </summary>
        public Dictionary<string, object>? CustomOptions { get; set; }
    }
}

