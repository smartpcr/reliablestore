//-------------------------------------------------------------------------------
// <copyright file="SynchronousMode.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.SQLite
{
    public enum SynchronousMode
    {
        /// <summary>
        /// SQLite does not invoke fsync() (or equivalent) on the database or journal file before a commit. Writes go to the OS cache only.
        /// cons: May lose or corrupt the last transactions on crash.
        /// typical use case: Bulk loads, non-critical data
        /// </summary>
        Off = 0,

        /// <summary>
        /// default, SQLite calls fsync() on the journal file at commit, but does not always fsync() the database file header.
        /// In DELETE mode it still truncates/deletes the journal.
        /// pros: Consistent DB file + rollback journal intact; may lose the very last commit if power fails at the wrong moment.
        /// cons: good balance of safety and speed.
        /// </summary>
        Normal = 1,

        /// <summary>
        /// SQLite calls fsync() on both the journal file and the database file before returning from COMMIT.
        /// Almost no risk of corruption, even on power-fail.
        /// Only used when transactional systems requiring maximum safety
        /// </summary>
        Full = 2,

        /// <summary>
        /// In addition to FULL, SQLite also issues extra syncs when writing rollback-journal pages (one fsync() per page write).
        /// Even safer in edge cases (filesystem bugs).
        /// Used when vry high-assurance workloads on flaky storage.
        /// </summary>
        Extra = 3
    }
}