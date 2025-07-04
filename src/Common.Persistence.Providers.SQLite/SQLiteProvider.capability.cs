// -----------------------------------------------------------------------
// <copyright file="SQLiteProvider.capability.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Providers.SQLite
{
    using Common.Persistence.Contract;

    public partial class SQLiteProvider<T> : BaseProvider<T>, IPersistenceProvider<T> where T : IEntity
    {
    }
}