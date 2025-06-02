//-------------------------------------------------------------------------------
// <copyright file="IIndexingProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Contract
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Index, mapping, and query operations for a storage provider.
    /// </summary>
    public interface IIndexingProvider<T> where T : IEntity
    {
        Task<IEnumerable<ProviderIndex>> GetIndexesAsync(CancellationToken cancellationToken = default);
        Task AddOrUpdateIndexAsync(ProviderIndex index, CancellationToken cancellationToken = default);
        Task RemoveIndexAsync(string indexName, CancellationToken cancellationToken = default);
        Task<IEnumerable<T>> QueryAsync(ProviderQuery query, CancellationToken cancellationToken = default);
    }
}

