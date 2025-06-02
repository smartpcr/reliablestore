//-------------------------------------------------------------------------------
// <copyright file="InMemoryProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.InMemory
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Persistence.Contract;
    using Microsoft.Extensions.Logging;

    public class InMemoryProvider<T> : ICrudStorageProvider<T> where T : IEntity
    {
        private readonly ConcurrentDictionary<string, CacheEntry<T>> cache;
        private readonly InMemoryOptions options;
        private readonly ILogger<InMemoryProvider<T>> logger;
        private readonly Timer? evictionTimer;

        public InMemoryProvider(InMemoryOptions options, ILogger<InMemoryProvider<T>> logger)
        {
            this.options = options;
            this.logger = logger;
            this.cache = new ConcurrentDictionary<string, CacheEntry<T>>();

            if (this.options.EnableEviction)
            {
                this.evictionTimer = new Timer(this.PerformEviction,
                    null,
                    this.options.EvictionInterval,
                    this.options.EvictionInterval);
            }
        }

        public Task<T?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            if (this.cache.TryGetValue(key, out var entry))
            {
                if (!entry.IsExpired(this.options.DefaultTTL))
                {
                    entry.UpdateLastAccessed();
                    return Task.FromResult<T?>(entry.Value);
                }

                // Remove expired entry
                this.cache.TryRemove(key, out _);
            }

            return Task.FromResult<T?>(default(T));
        }

        public async Task<IEnumerable<T>> GetManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            var results = new List<T>();

            foreach (var key in keys)
            {
                var entity = await this.GetAsync(key, cancellationToken);
                if (entity != null)
                    results.Add(entity);
            }

            return results;
        }

        public Task<IEnumerable<T>> GetAllAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            var validEntries = this.cache.Values
                .Where(entry => !entry.IsExpired(this.options.DefaultTTL))
                .Select(entry => entry.Value);

            if (predicate != null)
            {
                var compiled = predicate.Compile();
                validEntries = validEntries.Where(compiled);
            }

            return Task.FromResult(validEntries.ToList().AsEnumerable());
        }

        public Task SaveAsync(string key, T entity, CancellationToken cancellationToken = default)
        {
            if (this.options.MaxCacheSize > 0 && this.cache.Count >= this.options.MaxCacheSize)
            {
                this.EvictLeastRecentlyUsed();
            }

            var entry = new CacheEntry<T>(entity);
            this.cache.AddOrUpdate(key, entry, (_, _) => entry);

            this.logger.LogDebug("Saved entity with key {Key}", key);
            return Task.CompletedTask;
        }

        public async Task SaveManyAsync(IEnumerable<KeyValuePair<string, T>> entities, CancellationToken cancellationToken = default)
        {
            var entityList = entities.ToList();

            // Check if we need to make space
            if (this.options.MaxCacheSize > 0)
            {
                var newCount = this.cache.Count + entityList.Count;
                var excessCount = newCount - this.options.MaxCacheSize;

                if (excessCount > 0)
                {
                    this.EvictMultiple(excessCount);
                }
            }

            foreach (var (key, entity) in entityList)
            {
                await this.SaveAsync(key, entity, cancellationToken);
            }
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            this.cache.TryRemove(key, out _);
            this.logger.LogDebug("Deleted entity with key {Key}", key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            if (this.cache.TryGetValue(key, out var entry))
            {
                if (!entry.IsExpired(this.options.DefaultTTL))
                {
                    return Task.FromResult(true);
                }

                // Remove expired entry
                this.cache.TryRemove(key, out _);
            }

            return Task.FromResult(false);
        }

        public async Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            var entities = await this.GetAllAsync(predicate, cancellationToken);
            return entities.Count();
        }

        private void PerformEviction(object? state)
        {
            try
            {
                var now = DateTime.UtcNow;
                var expiredKeys = this.cache
                    .Where(kvp => kvp.Value.IsExpired(this.options.DefaultTTL))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    this.cache.TryRemove(key, out _);
                }

                if (expiredKeys.Count > 0)
                {
                    this.logger.LogDebug("Evicted {Count} expired entries", expiredKeys.Count);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "Error during cache eviction");
            }
        }

        private void EvictLeastRecentlyUsed()
        {
            var oldestEntry = this.cache
                .OrderBy(kvp => kvp.Value.LastAccessed)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(oldestEntry.Key))
            {
                this.cache.TryRemove(oldestEntry.Key, out _);
                this.logger.LogDebug("Evicted LRU entry with key {Key}", oldestEntry.Key);
            }
        }

        private void EvictMultiple(int count)
        {
            var entriesToEvict = this.cache
                .OrderBy(kvp => kvp.Value.LastAccessed)
                .Take(count)
                .ToList();

            foreach (var entry in entriesToEvict)
            {
                this.cache.TryRemove(entry.Key, out _);
            }

            this.logger.LogDebug("Evicted {Count} LRU entries", entriesToEvict.Count);
        }

        public void Dispose()
        {
            this.evictionTimer?.Dispose();
            this.cache.Clear();
        }
    }

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

    public class InMemoryOptions
    {
        public TimeSpan? DefaultTTL { get; set; }
        public int MaxCacheSize { get; set; } = 10000;
        public bool EnableEviction { get; set; } = true;
        public TimeSpan EvictionInterval { get; set; } = TimeSpan.FromMinutes(5);
        public string EvictionStrategy { get; set; } = "LRU";
    }
}