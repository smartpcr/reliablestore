//-------------------------------------------------------------------------------
// <copyright file="IRepository.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Repository interface for entity operations in transactions
    /// </summary>
    public interface IRepository<T> where T : class
    {
        Task<T> GetAsync(string key, CancellationToken cancellationToken = default);
        Task<IEnumerable<T>> GetManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);
        Task<IEnumerable<T>> GetAllAsync(Func<T, bool> predicate = null, CancellationToken cancellationToken = default);
        Task<T> SaveAsync(string key, T entity, CancellationToken cancellationToken = default);
        Task SaveManyAsync(IDictionary<string, T> entities, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
        Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
    }
}