//-------------------------------------------------------------------------------
// <copyright file="Savepoint.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    using System;

    /// <summary>
    /// Savepoint implementation
    /// </summary>
    internal class Savepoint : ISavepoint
    {
        public string Name { get; }
        public string TransactionId { get; }
        public DateTime CreatedAt { get; }

        public Savepoint(string name, string transactionId, DateTime createdAt)
        {
            Name = name;
            TransactionId = transactionId;
            CreatedAt = createdAt;
        }
    }
}