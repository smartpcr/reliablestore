//-------------------------------------------------------------------------------
// <copyright file="JournalMode.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.SQLite
{
    public enum JournalMode
    {
        /// <summary>
        /// The rollback journal file is created at the start of each transaction and deleted when the transaction commits.
        /// This is the simplest and most portable mode (default).
        /// pros:
        /// 1. Maximum safety: full ability to roll back on crash
        /// 2. Works on all filesystems.
        /// cons:
        /// File‐system overhead deleting the journal on every commit.
        /// </summary>
        Delete,

        /// <summary>
        /// Similar to DELETE, but instead of deleting the journal file, SQLite truncates it to zero length at commit.
        /// On many platforms, truncating is faster than deleting because it avoids directory‐update overhead.
        /// pros: Lower overhead than DELETE on filesystems where truncation beats deletion.
        /// cons: Journal file still occupies a directory entry.
        /// </summary>
        Truncate,

        /// <summary>
        /// The journal file remains on disk, but its header is overwritten with zeros rather than deleted or truncated.
        /// This prevents other connections from rolling back using the old journal, yet avoids file‐creation/deletion costs
        /// pros:
        /// 1. Very low file‐system churn (no create/truncate/delete).
        /// 2. Good for environments where file‐ops are expensive.
        /// cons:
        /// Journal file may grow large over time (e.g. after VACUUM).
        /// </summary>
        Persist,

        /// <summary>
        /// The rollback journal is kept entirely in RAM, never on disk. This eliminates disk I/O for journaling—but
        /// if your process crashes mid-transaction, the database can become corrupt since there's no on-disk journal
        /// to recover from.
        /// pros:
        /// 1. Fastest possible commits (no disk writes).
        /// 2. Ideal for ephemeral or in-memory databases.
        /// cons:
        /// 1. No crash recovery.
        /// 2. Not suitable for persistent data.
        /// </summary>
        Memory,

        /// <summary>
        /// Write-Ahead Logging mode ("WAL") is an alternative journaling scheme that can dramatically improve concurrency and,
        /// in many cases, write performance. Under the hood, WAL shifts from the default "rollback-journal" (DELETE) mode—
        /// where updates are journaled by making a copy of the original page before modifying—to an append-only log of changes
        /// that is kept in a separate "-wal" file.
        /// pros:
        /// 1. Concurrent readers never block a writer, and a writer doesn't block readers, giving much higher concurrency.
        /// 2. Fewer fsync() calls—better performance on flaky filesystems.
        /// cons:
        /// 1. You must distribute three files (.db, -wal, -shm).
        /// 2. WAL files can grow large without regular checkpoints.
        /// 3. Not supported on some network‐mounted filesystems.
        /// </summary>
        WAL,

        /// <summary>
        /// Disables the rollback journal entirely. No journal file is ever created, and ROLLBACK no longer works
        /// (its behavior is undefined).
        /// If a crash occurs mid-transaction, the database is very likely to corrupt.
        /// pros: Zero journaling overhead.
        /// cons:
        /// 1. No atomic commit/rollback guarantees.
        /// 2. Database corruption is almost certain on failure.
        /// </summary>
        Off
    }
}