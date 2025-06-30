//-------------------------------------------------------------------------------
// <copyright file="ClusterRegistryStoreSettings.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry
{
    using Common.Persistence.Configuration;

    public class ClusterRegistryStoreSettings : CrudStorageProviderSettings
    {
        public override string Name { get; set; } = "ClusterRegistry";
        public override string AssemblyName { get; set; } = typeof(ClusterRegistryStoreSettings).Assembly.FullName!;
        public override string TypeName { get; set; } = typeof(ClusterRegistryProvider<>).FullName!;
        public override bool Enabled { get; set; } = true;
        
        /// <summary>
        /// Gets or sets the cluster name. If null, connects to local cluster.
        /// </summary>
        public string? ClusterName { get; set; }
        
        /// <summary>
        /// Gets or sets the application name for organizing data in cluster registry.
        /// </summary>
        public string ApplicationName { get; set; } = "ReliableStore";
        
        /// <summary>
        /// Gets or sets the service name for organizing data in cluster registry.
        /// </summary>
        public string ServiceName { get; set; } = "ClusterRegistry";
        
        /// <summary>
        /// Gets or sets the root registry path.
        /// </summary>
        public string RootPath { get; set; } = @"Software\Microsoft\ReliableStore";
        
        /// <summary>
        /// Gets or sets whether to enable compression for stored values.
        /// </summary>
        public bool EnableCompression { get; set; } = true;
        
        /// <summary>
        /// Gets or sets the maximum value size in KB (cluster registry limit is typically 64KB).
        /// </summary>
        public int MaxValueSizeKB { get; set; } = 64;
        
        /// <summary>
        /// Gets or sets the connection timeout in seconds.
        /// </summary>
        public int ConnectionTimeoutSeconds { get; set; } = 30;
        
        /// <summary>
        /// Gets or sets the retry count for transient failures.
        /// </summary>
        public int RetryCount { get; set; } = 3;
        
        /// <summary>
        /// Gets or sets the retry delay in milliseconds.
        /// </summary>
        public int RetryDelayMilliseconds { get; set; } = 100;
    }
}