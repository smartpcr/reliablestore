// -----------------------------------------------------------------------
// <copyright file="ClusterRegistryProvider.capability.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry
{
    using Common.Persistence.Contract;

    public partial class ClusterRegistryProvider<T> : BaseProvider<T>, IPersistenceProvider<T> where T : IEntity
    {
    }
}