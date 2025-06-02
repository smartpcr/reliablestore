// -----------------------------------------------------------------------
// <copyright file="BaseEntity.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Contract
{
    using System;
    using System.Collections.Generic;

    public class BaseEntity : IEntity
    {
        public string Id { get; set; }
        public string Name { get; set; }

        public string Key => $"{this.GetType().Name}/{this.Id}";
        public long Version { get; set; } = 1;
        public string? ETag { get; set; } = null;

        public IReadOnlyDictionary<string, object>? IndexFields => new Dictionary<string, object>()
        {
            { nameof(this.Id), this.Id },
            { nameof(this.Name), this.Name }
        };
        public IReadOnlyList<Uri>? ChangeSubscriptions { get; set; }
        public DateTimeOffset? CheckoutDate { get; set;}
        public DateTimeOffset? CheckoutExpiry { get; set;}
    }
}