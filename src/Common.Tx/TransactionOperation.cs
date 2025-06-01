//-------------------------------------------------------------------------------
// <copyright file="TransactionOperation.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    using System;

    /// <summary>
    /// Represents a pending transaction operation
    /// </summary>
    internal class TransactionOperation<T>
    {
        public string TransactionId { get; set; }
        public string Key { get; set; }
        public OperationType Type { get; set; }
        public T OriginalValue { get; set; }
        public T NewValue { get; set; }
        public DateTime Timestamp { get; set; }

        public TransactionOperation<T> Clone()
        {
            // Perform a shallow copy, assuming T is a reference type and its state is not changing, or it's immutable/DTO.
            // If T is mutable and its state changes are part of the transaction, a deep clone might be needed.
            return (TransactionOperation<T>)this.MemberwiseClone();
        }
    }
}