//-------------------------------------------------------------------------------
// <copyright file="IMetadataProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Contract
{
    /// <summary>
    /// Provider metadata contract.
    /// </summary>
    public interface IMetadataProvider<T> where T : IEntity
    {
        EntityMetadata<T> GetMetadata(IEntity entity);
        EntityMetadata<T> ReadMetadataFromStore(string entityKey);
        void WriteMetadataToStore(EntityMetadata<T> metadata);
    }
}

