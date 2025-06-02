// -----------------------------------------------------------------------
// <copyright file="FileSystemProvider.capability.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Providers.FileSystem
{
    using Common.Persistence.Contract;

    public partial class FileSystemProvider<T> : BaseProvider<T>, IPersistenceProvider<T> where T : IEntity
    {
    }
}