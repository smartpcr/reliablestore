// -----------------------------------------------------------------------
// <copyright file="CacheEntry.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Common.Persistence.Providers.InMemory
{
    using System;

    public class CacheEntry<T>
    {
        public T Value { get; }
        public DateTime CreatedAt { get; }
        public DateTime LastAccessed { get; private set; }
        public DateTime? ExplicitExpiry { get; set; }

        public CacheEntry(T value, DateTime? explicitExpiry = null)
        {
            this.Value = value;
            this.CreatedAt = DateTime.UtcNow;
            this.LastAccessed = this.CreatedAt;
            this.ExplicitExpiry = explicitExpiry;
        }

        public void UpdateLastAccessed()
        {
            this.LastAccessed = DateTime.UtcNow;
        }

        public bool IsExpired(TimeSpan? defaultTTL)
        {
            if (this.ExplicitExpiry.HasValue)
                return DateTime.UtcNow > this.ExplicitExpiry.Value;

            if (defaultTTL.HasValue)
                return DateTime.UtcNow > this.CreatedAt.Add(defaultTTL.Value);

            return false;
        }
    }
}