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
        public override string TypeName { get; set; } = typeof(FileSystemProvider<>).FullName!;
        public override bool Enabled { get; set; } = true;

        /// <summary>
        /// The file path is now used to determine the root directory for entity files.
        /// Each entity will be stored in a separate file under this directory.
        /// </summary>
        public string FolderPath { get; set; } = "data";
        
        /// <summary>
        /// Maximum number of concurrent file operations.
        /// Defaults to ProcessorCount * 2.
        /// </summary>
        public int? MaxConcurrentFiles { get; set; }
        
        /// <summary>
        /// Maximum number of retries for file operations.
        /// </summary>
        public int MaxRetries { get; set; } = 3;
        
        /// <summary>
        /// Delay between retries in milliseconds.
        /// </summary>
        public int RetryDelayMs { get; set; } = 100;
        
        /// <summary>
        /// Whether to use subdirectories for better file system performance.
        /// When true, entities are stored in subdirectories based on key prefix.
        /// </summary>
        public bool UseSubdirectories { get; set; } = true;
        
        // Legacy backup settings - not currently used in new implementation
        public string? BackupDirectory { get; set; }
        public int BackupRetentionDays { get; set; } = 7;
        public bool EnableBackups { get; set; } = false;
    }
}