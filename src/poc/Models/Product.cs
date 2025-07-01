// -----------------------------------------------------------------------
// <copyright file="Product.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Models
{
    using System.Collections.Generic;
    using Common.Persistence.Contract;

    public class Product : BaseEntity
    {
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public string? Description { get; set; }
        public List<string>? Tags { get; set; }
    }
}