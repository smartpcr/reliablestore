// -----------------------------------------------------------------------
// <copyright file="ClusterAccessRights.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Api
{
    using System;

    /// <summary>
    /// Registry access rights.
    /// </summary>
    [Flags]
    internal enum ClusterAccessRights : uint
    {
        QueryValue = 0x0001,
        SetValue = 0x0002,
        CreateSubKey = 0x0004,
        EnumerateSubKeys = 0x0008,
        Notify = 0x0010,
        CreateLink = 0x0020,
        Delete = 0x00010000,
        ReadControl = 0x00020000,
        WriteDac = 0x00040000,
        WriteOwner = 0x00080000,
        Synchronize = 0x00100000,
        Read = ClusterAccessRights.ReadControl | ClusterAccessRights.QueryValue | ClusterAccessRights.EnumerateSubKeys | ClusterAccessRights.Notify,
        Write = ClusterAccessRights.ReadControl | ClusterAccessRights.SetValue | ClusterAccessRights.CreateSubKey,
        Execute = ClusterAccessRights.Read,
        AllAccess = 0x000F003F,
        MaximumAllowed = 0x02000000,
        GenericRead = 0x80000000,
        GenericAll = 0x10000000
    }
}