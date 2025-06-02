// -----------------------------------------------------------------------
// <copyright file="BaseProviderSettings.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Configuration
{
    public abstract class BaseProviderSettings
    {
        /// <summary>
        /// Gets or sets the provider name (unique identifier).
        /// </summary>
        public abstract string Name { get; set; }

        /// <summary>
        /// Gets or sets the provider assembly name.
        /// </summary>
        public abstract string AssemblyName { get; set; }

        /// <summary>
        /// Gets or sets the provider type (e.g., FileSystem, InMemory, Sql, etc).
        /// </summary>
        public abstract string TypeName { get; set; }

        /// <summary>
        /// Gets or sets whether provider is enabled.
        /// </summary>
        public abstract bool Enabled { get; set; }
    }
}