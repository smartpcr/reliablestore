// -----------------------------------------------------------------------
// <copyright file="ClusterErrorCode.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Api
{

    /// <summary>
    /// Cluster API error codes.
    /// </summary>
    internal enum ClusterErrorCode
    {
        Success = 0,
        FileNotFound = 2,
        AccessDenied = 5,
        InvalidHandle = 6,
        NoMoreItems = 259,
        MoreData = 234,
        ClusterNodeDown = 5050,
        ClusterNodeNotAvailable = 5036,
        ClusterNoQuorum = 5925
    }
}