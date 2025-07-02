// -----------------------------------------------------------------------
// <copyright file="InMemoryProvider.capability.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Providers.InMemory
{
    using System;
    using Common.Persistence.Contract;

    public partial class InMemoryProvider<T> : BaseProvider<T>, IPersistenceProvider<T> where T : IEntity
    {
    }
}