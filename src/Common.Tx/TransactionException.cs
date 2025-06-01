//-------------------------------------------------------------------------------
// <copyright file="TransactionException.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    using System;

    /// <summary>
    /// Transaction-specific exception
    /// </summary>
    public class TransactionException : Exception
    {
        public TransactionState? TransactionState { get; }

        public TransactionException(string message) : base(message) { }
        public TransactionException(string message, Exception innerException) : base(message, innerException) { }
        public TransactionException(string message, TransactionState state) : base(message) 
        {
            TransactionState = state;
        }
        public TransactionException(string message, TransactionState state, Exception innerException) : base(message, innerException) 
        {
            TransactionState = state;
        }
    }

    /// <summary>
    /// Transaction timeout-specific exception
    /// </summary>
    public class TransactionTimeoutException : TransactionException
    {
        public TransactionTimeoutException(string message) : base(message, Common.Tx.TransactionState.Timeout) { }
        public TransactionTimeoutException(string message, Exception innerException) : base(message, Common.Tx.TransactionState.Timeout, innerException) { }
    }
}