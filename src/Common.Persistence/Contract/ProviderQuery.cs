//-------------------------------------------------------------------------------
// <copyright file="ProviderQuery.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Contract
{
    /// <summary>
    /// Represents a provider-agnostic query using OData syntax for filtering, sorting, and projection.
    /// </summary>
    public sealed class ProviderQuery
    {
        /// <summary>
        /// Gets or sets the OData filter string (e.g., "$filter=Name eq 'John'").
        /// </summary>
        public string? Filter { get; set; }

        /// <summary>
        /// Gets or sets the OData orderby string (e.g., "$orderby=Created desc").
        /// </summary>
        public string? OrderBy { get; set; }

        /// <summary>
        /// Gets or sets the OData select string (e.g., "$select=Id,Name").
        /// </summary>
        public string? Select { get; set; }

        /// <summary>
        /// Gets or sets the number of records to skip (OData $skip).
        /// </summary>
        public int? Skip { get; set; }

        /// <summary>
        /// Gets or sets the number of records to take (OData $top).
        /// </summary>
        public int? Top { get; set; }
    }
}

