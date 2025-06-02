// -----------------------------------------------------------------------
// <copyright file="Customer.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Models
{
    using Common.Persistence.Contract;

    public class Customer : BaseEntity
    {
        public string Email { get; set; } = string.Empty;
    }
}