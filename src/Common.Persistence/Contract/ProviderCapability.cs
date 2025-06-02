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
        Crud = 0,
        Index = 1 << 0,
        Archive = 1 << 1,
        Purge = 1 << 2,
        Backup = 1 << 3,
        Health = 1 << 4,
        Migration = 1 << 5,
        // Add more as needed
    }
}

