// -----------------------------------------------------------------------
// <copyright file="FileSystemOptions.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Providers.FileSystem
{
    using Common.Persistence.Configuration;

    public class FileSystemStoreSettings : CrudStorageProviderSettings
    {
        public override string Name { get; set; } = "FileSystem";
        public override string AssemblyName { get; set; } = typeof(FileSystemStoreSettings).Assembly.FullName!;
        public override string TypeName { get; set; } = typeof(FileSystemProvider<>).Name;
        public override bool Enabled { get; set; } = true;

        public string FilePath { get; set; } = "data/entities.json";
        public string? BackupDirectory { get; set; }
        public int BackupRetentionDays { get; set; } = 7;
        public bool EnableBackups { get; set; } = false;
    }
}