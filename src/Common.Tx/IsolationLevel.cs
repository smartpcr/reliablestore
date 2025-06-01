//-------------------------------------------------------------------------------
// <copyright file="IsolationLevel.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    /// <summary>
    /// Transaction isolation levels
    /// </summary>
    public enum IsolationLevel
    {
        ReadUncommitted,
        ReadCommitted,
        RepeatableRead,
        Snapshot,
        Serializable
    }
}