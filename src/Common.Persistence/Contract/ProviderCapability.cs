//-------------------------------------------------------------------------------
// <copyright file="ProviderCapabilities.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Contract
{
    using System;

    /// <summary>
    /// Flags for provider capabilities (additive).
    /// </summary>
    [Flags]
    public enum ProviderCapability
    {
        None = 0,
        Crud = 1 << 0,
        Index = 1 << 1,
        Archive = 1 << 2,
        Purge = 1 << 3,
        Backup = 1 << 4,
        Health = 1 << 5,
        Migration = 1 << 6,
        // Add more as needed
    }
}

