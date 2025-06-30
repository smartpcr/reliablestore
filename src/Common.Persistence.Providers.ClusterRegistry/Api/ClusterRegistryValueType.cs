// -----------------------------------------------------------------------
// <copyright file="ClusterRegistryValueType.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Api
{
    /// <summary>
    /// Cluster registry value types.
    /// </summary>
    internal enum ClusterRegistryValueType : uint
    {
        None = 0,
        String = 1,
        ExpandString = 2,
        Binary = 3,
        DWord = 4,
        DWordBigEndian = 5,
        Link = 6,
        MultiString = 7,
        ResourceList = 8,
        FullResourceDescriptor = 9,
        ResourceRequirementsList = 10,
        QWord = 11
    }
}