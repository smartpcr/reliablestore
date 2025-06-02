//-------------------------------------------------------------------------------
// <copyright file="IPersistenceProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Contract
{
    using System.Collections.Generic;

    /// <summary>
    /// Composite storage provider interface.
    /// </summary>
    public interface IPersistenceProvider<T> where T : IEntity
    {
        ProviderCapability Capabilities { get; }

        ICrudStorageProvider<T> CrudProvider { get; }
        IIndexingProvider<T> IndexingProvider { get; }
        IArchivalProvider<T> ArchivalProvider { get; }
        IPurgeProvider<T> PurgeProvider { get; }
        IBackupProvider<T> BackupProvider { get; }
        IMigrationProvider<T> MigrationProvider { get; }
    }
}

