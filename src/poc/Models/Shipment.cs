//-------------------------------------------------------------------------------
// <copyright file="Shipment.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Models
{
    using Common.Persistence.Contract;

    public class Shipment: BaseEntity
    {
        public string Id { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public string TrackingNumber { get; set; } = string.Empty;
    }
}