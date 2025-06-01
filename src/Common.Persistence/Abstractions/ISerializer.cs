//-------------------------------------------------------------------------------
// <copyright file="ISerializer.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Abstractions
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    public interface ISerializer<T>
    {
        Task<byte[]> SerializeAsync(T entity, CancellationToken cancellationToken = default);
        Task<T?> DeserializeAsync(byte[] data, CancellationToken cancellationToken = default);
        Task SerializeToFileAsync(T entity, string filePath, CancellationToken cancellationToken = default);
        Task<T?> DeserializeFromFileAsync(string filePath, CancellationToken cancellationToken = default);
        Task SerializeDictionaryToFileAsync(IDictionary<string, T> entities, string filePath, CancellationToken cancellationToken = default);
        Task<IDictionary<string, T>> DeserializeDictionaryFromFileAsync(string filePath, CancellationToken cancellationToken = default);
    }
}
