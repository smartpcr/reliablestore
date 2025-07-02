//-------------------------------------------------------------------------------
// <copyright file="EsentProvider.capability.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.Esent
{
    using System;
    using Common.Persistence.Contract;

    public partial class EsentProvider<T> : BaseProvider<T>, IPersistenceProvider<T> where T : IEntity
    {
    }
}