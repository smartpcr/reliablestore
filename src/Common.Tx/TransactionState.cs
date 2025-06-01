//-------------------------------------------------------------------------------
// <copyright file="TransactionState.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    /// <summary>
    /// Transaction state enumeration
    /// </summary>
    public enum TransactionState
    {
        Active,
        Preparing,
        Prepared,
        Committing,
        Committed,
        RollingBack,
        RolledBack,
        Failed,
        Timeout
    }
}