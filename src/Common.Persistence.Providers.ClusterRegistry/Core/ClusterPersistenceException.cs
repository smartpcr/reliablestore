//-------------------------------------------------------------------------------
// <copyright file="ClusterPersistenceException.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Core
{
    using System;

    /// <summary>
    /// Base exception for cluster persistence operations.
    /// </summary>
    [Serializable]
    public class ClusterPersistenceException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterPersistenceException"/> class.
        /// </summary>
        public ClusterPersistenceException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterPersistenceException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        public ClusterPersistenceException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterPersistenceException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public ClusterPersistenceException(string message, Exception innerException) : base(message, innerException)
        {
        }

#if NETFRAMEWORK
        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterPersistenceException"/> class.
        /// </summary>
        /// <param name="info">Serialization info.</param>
        /// <param name="context">Streaming context.</param>
        protected ClusterPersistenceException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// Exception thrown when cluster connection fails.
    /// </summary>
    [Serializable]
    public class ClusterConnectionException : ClusterPersistenceException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterConnectionException"/> class.
        /// </summary>
        public ClusterConnectionException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterConnectionException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        public ClusterConnectionException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterConnectionException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public ClusterConnectionException(string message, Exception innerException) : base(message, innerException)
        {
        }

#if NETFRAMEWORK
        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterConnectionException"/> class.
        /// </summary>
        /// <param name="info">Serialization info.</param>
        /// <param name="context">Streaming context.</param>
        protected ClusterConnectionException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// Exception thrown when cluster operations fail due to access rights.
    /// </summary>
    [Serializable]
    public class ClusterAccessDeniedException : ClusterPersistenceException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterAccessDeniedException"/> class.
        /// </summary>
        public ClusterAccessDeniedException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterAccessDeniedException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        public ClusterAccessDeniedException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterAccessDeniedException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public ClusterAccessDeniedException(string message, Exception innerException) : base(message, innerException)
        {
        }

#if NETFRAMEWORK
        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterAccessDeniedException"/> class.
        /// </summary>
        /// <param name="info">Serialization info.</param>
        /// <param name="context">Streaming context.</param>
        protected ClusterAccessDeniedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
    }


    /// <summary>
    /// Exception thrown when transaction operations fail.
    /// </summary>
    [Serializable]
    public class ClusterTransactionException : ClusterPersistenceException
    {
        /// <summary>
        /// Gets the index of the failed operation in the transaction.
        /// </summary>
        public int FailedOperationIndex { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterTransactionException"/> class.
        /// </summary>
        public ClusterTransactionException()
        {
            this.FailedOperationIndex = -1;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterTransactionException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="failedOperationIndex">The index of the failed operation.</param>
        public ClusterTransactionException(string message, int failedOperationIndex = -1) : base(message)
        {
            this.FailedOperationIndex = failedOperationIndex;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterTransactionException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        /// <param name="failedOperationIndex">The index of the failed operation.</param>
        public ClusterTransactionException(string message, Exception innerException, int failedOperationIndex = -1) : base(message, innerException)
        {
            this.FailedOperationIndex = failedOperationIndex;
        }

#if NETFRAMEWORK
        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterTransactionException"/> class.
        /// </summary>
        /// <param name="info">Serialization info.</param>
        /// <param name="context">Streaming context.</param>
        protected ClusterTransactionException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            this.FailedOperationIndex = info.GetInt32(nameof(FailedOperationIndex));
        }

        /// <inheritdoc />
        [Obsolete("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.")]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(FailedOperationIndex), this.FailedOperationIndex);
        }
#endif
    }
}