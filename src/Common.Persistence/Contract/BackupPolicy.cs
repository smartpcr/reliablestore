// -----------------------------------------------------------------------
// <copyright file="BackupPolicy.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Contract
{
    using System;

    /// <summary>
    /// Configuration for backup policy (schedule, retention, location, etc).
    /// </summary>
    public sealed class BackupPolicy<T> where T : IEntity
    {
        /// <summary>
        /// Gets or sets the cron schedule for backup jobs (e.g., "0 2 * * *" for daily at 2am).
        /// </summary>
        public string Schedule { get; set; } = "0 2 * * *";

        /// <summary>
        /// Gets or sets the backup retention period.
        /// </summary>
        public TimeSpan Retention { get; set; } = TimeSpan.FromDays(30);

        /// <summary>
        /// Gets or sets the backup location (e.g., path, bucket, URI).
        /// </summary>
        public string Location { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets additional custom policy options.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, object>? CustomOptions { get; set; }
    }
}