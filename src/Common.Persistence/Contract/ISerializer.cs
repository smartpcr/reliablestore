//-------------------------------------------------------------------------------
// <copyright file="ISerializer.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Contract
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    public interface ISerializer<T> where T : IEntity
    {
        Task<byte[]> SerializeAsync(T entity, CancellationToken cancellationToken = default);
        Task<byte[]> SerializeDictionaryAsync(Dictionary<string, T> dictionary, CancellationToken cancellationToken = default);
        Task<T?> DeserializeAsync(byte[] data, CancellationToken cancellationToken = default);
        Task<Dictionary<string, T>> DeserializeDictionaryAsync(byte[] data, CancellationToken cancellationToken = default);


        /// <summary>
        /// Serialize entity metadata to byte array.
        /// </summary>
        Task<byte[]> SerializeMetadataAsync(EntityMetadata<T> metadata, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deserialize entity metadata from byte array.
        /// </summary>
        Task<EntityMetadata<T>> DeserializeMetadataAsync(byte[] data, CancellationToken cancellationToken = default);

        /// <summary>
        /// Serialize entity metadata to file.
        /// </summary>
        Task SerializeMetadataToFileAsync(EntityMetadata<T> metadata, string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deserialize entity metadata from file.
        /// </summary>
        Task<EntityMetadata<T>> DeserializeMetadataFromFileAsync(string filePath, CancellationToken cancellationToken = default);
    }
}
