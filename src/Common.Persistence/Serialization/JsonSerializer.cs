// -----------------------------------------------------------------------
// <copyright file="JsonSerializer.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Serialization
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Persistence.Contract;
    using Newtonsoft.Json;

    public class JsonSerializer<T> : ISerializer<T> where T : IEntity
    {
        public Task<byte[]> SerializeAsync(T entity, CancellationToken cancellationToken = default)
        {
            var json = JsonConvert.SerializeObject(entity);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            return Task.FromResult(bytes);
        }

        public Task<byte[]> SerializeDictionaryAsync(Dictionary<string, T> dictionary, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }

        public Task<T?> DeserializeAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            var json = System.Text.Encoding.UTF8.GetString(data);
            var entity = JsonConvert.DeserializeObject<T>(json);
            return Task.FromResult(entity);
        }

        public Task<Dictionary<string, T>> DeserializeDictionaryAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }

        public Task<byte[]> SerializeMetadataAsync(EntityMetadata<T> metadata, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }

        public Task<EntityMetadata<T>> DeserializeMetadataAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }

        public Task SerializeMetadataToFileAsync(EntityMetadata<T> metadata, string filePath, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }

        public Task<EntityMetadata<T>> DeserializeMetadataFromFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }
    }
}