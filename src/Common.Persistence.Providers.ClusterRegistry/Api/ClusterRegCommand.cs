// -----------------------------------------------------------------------
// <copyright file="ClusterRegCommand.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Api
{

    /// <summary>
    /// Cluster registry batch commands.
    /// </summary>
    internal enum ClusterRegCommand
    {
        None = 0,
        SetValue = 1,
        CreateKey = 2,
        DeleteKey = 3,
        DeleteValue = 4,
        SetKeySecurity = 5,
        ValueDeleted = 6
    }

}