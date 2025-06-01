//-------------------------------------------------------------------------------
// <copyright file="ISavepoint.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    using System;

    /// <summary>
    /// Savepoint interface for partial rollback
    /// </summary>
    public interface ISavepoint
    {
        string Name { get; }
        string TransactionId { get; }
        DateTime CreatedAt { get; }
    }
}