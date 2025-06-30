//-------------------------------------------------------------------------------
// <copyright file="ClusterPersistenceConfiguration.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Core
{
    using System;

    /// <summary>
    /// Configuration options for cluster registry persistence.
    /// </summary>
    public sealed class ClusterPersistenceConfiguration
    {
        /// <summary>
        /// Gets or sets the cluster name. Use null for the local cluster.
        /// </summary>
        public string? ClusterName { get; set; }

        /// <summary>
        /// Gets or sets the root registry path for this application.
        /// Default is "SOFTWARE\\ClusterApps".
        /// </summary>
        public string RootPath { get; set; } = "SOFTWARE\\ClusterApps";

        /// <summary>
        /// Gets or sets the application name used to scope registry entries.
        /// This becomes a subkey under RootPath.
        /// </summary>
        public string ApplicationName { get; set; } = "DefaultApp";

        /// <summary>
        /// Gets or sets the service name used to further scope registry entries.
        /// This becomes a subkey under ApplicationName.
        /// </summary>
        public string ServiceName { get; set; } = "DefaultService";

        /// <summary>
        /// Gets or sets the timeout for cluster operations.
        /// Default is 30 seconds.
        /// </summary>
        public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets the number of retry attempts for failed operations.
        /// Default is 3.
        /// </summary>
        public int RetryAttempts { get; set; } = 3;

        /// <summary>
        /// Gets or sets the delay between retry attempts.
        /// Default is 1 second.
        /// </summary>
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Gets or sets a value indicating whether to use exponential backoff for retries.
        /// Default is true.
        /// </summary>
        public bool UseExponentialBackoff { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to compress serialized values.
        /// Large values benefit from compression, but it adds CPU overhead.
        /// Default is false.
        /// </summary>
        public bool EnableCompression { get; set; } = false;

        /// <summary>
        /// Gets or sets the maximum size in bytes for values before they are rejected.
        /// Windows registry has limits on value sizes.
        /// Default is 1MB.
        /// </summary>
        public int MaxValueSize { get; set; } = 1024 * 1024; // 1MB

        /// <summary>
        /// Gets the full registry path for this configuration.
        /// </summary>
        /// <returns>The complete registry path.</returns>
        public string GetFullPath()
        {
            return $"{this.RootPath}\\{this.ApplicationName}\\{this.ServiceName}";
        }

        /// <summary>
        /// Gets the full registry path for a specific collection.
        /// </summary>
        /// <param name="collectionName">The collection name.</param>
        /// <returns>The complete registry path for the collection.</returns>
        public string GetCollectionPath(string collectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
            {
                throw new ArgumentException("Collection name cannot be null or whitespace.", nameof(collectionName));
            }

            return $"{this.GetFullPath()}\\{collectionName}";
        }

        /// <summary>
        /// Validates the configuration and throws an exception if invalid.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the configuration is invalid.</exception>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(this.RootPath))
            {
                throw new InvalidOperationException("RootPath cannot be null or whitespace.");
            }

            if (string.IsNullOrWhiteSpace(this.ApplicationName))
            {
                throw new InvalidOperationException("ApplicationName cannot be null or whitespace.");
            }

            if (string.IsNullOrWhiteSpace(this.ServiceName))
            {
                throw new InvalidOperationException("ServiceName cannot be null or whitespace.");
            }

            if (this.OperationTimeout <= TimeSpan.Zero)
            {
                throw new InvalidOperationException("OperationTimeout must be positive.");
            }

            if (this.RetryAttempts < 0)
            {
                throw new InvalidOperationException("RetryAttempts cannot be negative.");
            }

            if (this.RetryDelay < TimeSpan.Zero)
            {
                throw new InvalidOperationException("RetryDelay cannot be negative.");
            }

            if (this.MaxValueSize <= 0)
            {
                throw new InvalidOperationException("MaxValueSize must be positive.");
            }
        }
    }
}