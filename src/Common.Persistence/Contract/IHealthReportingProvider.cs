//-------------------------------------------------------------------------------
// <copyright file="IHealthReportingProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Contract
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Health reporting for a storage provider.
    /// </summary>
    public interface IHealthReportingProvider
    {
        Task<ProviderHealth> HealthCheckAsync(CancellationToken cancellationToken = default);
    }
}

