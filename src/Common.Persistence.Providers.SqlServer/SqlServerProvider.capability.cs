//-------------------------------------------------------------------------------
// <copyright file="SqlServerProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.SqlServer
{
    using Common.Persistence.Contract;

    public partial class SqlServerProvider<T> : BaseProvider<T>, IPersistenceProvider<T> where T : IEntity
    {
    }
}