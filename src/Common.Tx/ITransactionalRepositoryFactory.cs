//-------------------------------------------------------------------------------
// <copyright file="ITransactionalRepositoryFactory.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx
{
    /// <summary>
    /// Factory for creating transactional repositories
    /// </summary>
    public interface ITransactionalRepositoryFactory
    {
        TransactionalRepository<T> CreateTransactionalRepository<T>(IRepository<T> underlyingRepository) where T : class;
    }
}