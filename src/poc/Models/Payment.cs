// -----------------------------------------------------------------------
// <copyright file="Payment.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Models
{
    using Common.Persistence.Contract;

    public class Payment : BaseEntity
    {
        public string OrderId { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = "Pending";
        public string PaymentMethod { get; set; } = string.Empty;
    }
}