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
    using Unity;

    public partial class InMemoryProvider<T> : ICrudStorageProvider<T> where T : IEntity
    {
        private readonly ConcurrentDictionary<string, CacheEntry<T>> cache;
        private readonly InMemoryStoreSettings settings;
        private readonly ILogger<InMemoryProvider<T>> logger;
        private readonly Timer? evictionTimer;

        public InMemoryProvider(IServiceProvider serviceProvider, string name)
            : base(serviceProvider, name)
        {
            this.settings = this.ConfigReader.ReadSettings<InMemoryStoreSettings>(name);
            this.logger = this.GetLogger<InMemoryProvider<T>>();
            this.cache = new ConcurrentDictionary<string, CacheEntry<T>>();

            if (this.settings.EnableEviction)
            {
                this.evictionTimer = new Timer(this.PerformEviction,
                    null,
                    this.settings.EvictionInterval,
                    this.settings.EvictionInterval);
            }
        }

        public InMemoryProvider(UnityContainer container, string name)
            : base(container, name)
        {
            this.settings = this.ConfigReader.ReadSettings<InMemoryStoreSettings>(name);
            this.logger = this.GetLogger<InMemoryProvider<T>>();
            this.cache = new ConcurrentDictionary<string, CacheEntry<T>>();

            if (this.settings.EnableEviction)
            {
                this.evictionTimer = new Timer(this.PerformEviction,
                    null,
                    this.settings.EvictionInterval,
                    this.settings.EvictionInterval);
            }
        }

        public Task<T?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            if (this.cache.TryGetValue(key, out var entry))
            {
                if (!entry.IsExpired(this.settings.DefaultTTL))
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
                .Where(entry => !entry.IsExpired(this.settings.DefaultTTL))
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
            if (this.settings.MaxCacheSize > 0 && this.cache.Count >= this.settings.MaxCacheSize)
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
            if (this.settings.MaxCacheSize > 0)
            {
                var newCount = this.cache.Count + entityList.Count;
                var excessCount = newCount - this.settings.MaxCacheSize;

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
                if (!entry.IsExpired(this.settings.DefaultTTL))
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

        public Task<long> ClearAsync(CancellationToken cancellationToken = default)
        {
            var count = this.cache.Count;
            this.cache.Clear();
            this.logger.LogDebug("Cleared all cache entries");
            return Task.FromResult((long)count);
        }

        private void PerformEviction(object? state)
        {
            try
            {
                var now = DateTime.UtcNow;
                var expiredKeys = this.cache
                    .Where(kvp => kvp.Value.IsExpired(this.settings.DefaultTTL))
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
}