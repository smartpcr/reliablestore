//-------------------------------------------------------------------------------
// <copyright file="IClusterPersistenceSerializer.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Serialization
{
    using System;

    /// <summary>
    /// Interface for serializing and deserializing objects for cluster persistence.
    /// </summary>
    public interface IClusterPersistenceSerializer
    {
        /// <summary>
        /// Serializes an object to a string representation.
        /// </summary>
        /// <typeparam name="T">The type of object to serialize.</typeparam>
        /// <param name="value">The value to serialize.</param>
        /// <returns>The serialized string representation.</returns>
        /// <exception cref="ClusterPersistenceSerializationException">Thrown when serialization fails.</exception>
        string Serialize<T>(T value);

        /// <summary>
        /// Deserializes a string representation to an object.
        /// </summary>
        /// <typeparam name="T">The type of object to deserialize to.</typeparam>
        /// <param name="serializedValue">The serialized string representation.</param>
        /// <returns>The deserialized object.</returns>
        /// <exception cref="ClusterPersistenceSerializationException">Thrown when deserialization fails.</exception>
        T Deserialize<T>(string serializedValue);

        /// <summary>
        /// Attempts to deserialize a string representation to an object.
        /// </summary>
        /// <typeparam name="T">The type of object to deserialize to.</typeparam>
        /// <param name="serializedValue">The serialized string representation.</param>
        /// <param name="value">The deserialized object if successful.</param>
        /// <returns>True if deserialization was successful, false otherwise.</returns>
        bool TryDeserialize<T>(string serializedValue, out T? value);
    }

    /// <summary>
    /// Exception thrown when serialization or deserialization fails.
    /// </summary>
    [System.Serializable]
    public class ClusterPersistenceSerializationException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterPersistenceSerializationException"/> class.
        /// </summary>
        public ClusterPersistenceSerializationException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterPersistenceSerializationException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        public ClusterPersistenceSerializationException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterPersistenceSerializationException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public ClusterPersistenceSerializationException(string message, Exception innerException) : base(message, innerException)
        {
        }

#if NETFRAMEWORK
        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterPersistenceSerializationException"/> class.
        /// </summary>
        /// <param name="info">Serialization info.</param>
        /// <param name="context">Streaming context.</param>
        protected ClusterPersistenceSerializationException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) : base(info, context)
        {
        }
#endif
    }
}