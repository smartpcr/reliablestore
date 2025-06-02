// -----------------------------------------------------------------------
// <copyright file="PersistenceProviderRegistration.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Configuration
{
    using System.Collections.Generic;
    using Common.Persistence.Contract;

    /// <summary>
    /// Represents a single provider registration entry.
    /// </summary>
    public class PersistenceProviderSettings : BaseProviderSettings
    {
        /// <summary>
        /// Gets or sets the provider capabilities (e.g., Index, Archive, Purge, Backup, etc).
        /// </summary>
        public Dictionary<ProviderCapability, string> Capabilities { get; set; }

        public override string Name { get; set; }
        public override string AssemblyName { get; set; }
        public override string TypeName { get; set; }
        public override bool Enabled { get; set; }
    }
}