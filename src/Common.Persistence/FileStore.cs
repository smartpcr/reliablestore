using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Tx;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Common.Persistence
{
    public class FileStore<T> : IRepository<T>, ITransactionalResource where T : class
    {
        private readonly string _filePath;
        private readonly ILogger<FileStore<T>> _logger;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private Dictionary<string, T> _cache;
        private readonly object _cacheLock = new object();

        public FileStore(string filePath, ILogger<FileStore<T>> logger)
        {
            _filePath = filePath;
            _logger = logger;
            EnsureFileExists();
            LoadCache();
        }

        private void EnsureFileExists()
        {
            if (!File.Exists(_filePath))
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(_filePath, JsonConvert.SerializeObject(new Dictionary<string, T>()));
            }
        }

        private void LoadCache()
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                _cache = JsonConvert.DeserializeObject<Dictionary<string, T>>(json) ?? new Dictionary<string, T>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load cache from file {FilePath}", _filePath);
                _cache = new Dictionary<string, T>();
            }
        }

        private async Task SaveToFileAsync()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_cache, Formatting.Indented);
                await File.WriteAllTextAsync(_filePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save to file {FilePath}", _filePath);
                throw;
            }
        }

        public async Task<T> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                lock (_cacheLock)
                {
                    return _cache.TryGetValue(key, out var value) ? value : null;
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<IEnumerable<T>> GetManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                lock (_cacheLock)
                {
                    return keys.Select(key => _cache.TryGetValue(key, out var value) ? value : null)
                               .Where(item => item != null);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<IEnumerable<T>> GetAllAsync(Func<T, bool> predicate = null, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                lock (_cacheLock)
                {
                    var values = _cache.Values;
                    return predicate != null ? values.Where(predicate) : values;
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<T> SaveAsync(string key, T entity, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                lock (_cacheLock)
                {
                    _cache[key] = entity;
                }
                await SaveToFileAsync();
                return entity;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task SaveManyAsync(IDictionary<string, T> entities, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                lock (_cacheLock)
                {
                    foreach (var kvp in entities)
                    {
                        _cache[kvp.Key] = kvp.Value;
                    }
                }
                await SaveToFileAsync();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                bool removed;
                lock (_cacheLock)
                {
                    removed = _cache.Remove(key);
                }
                if (removed)
                {
                    await SaveToFileAsync();
                }
                return removed;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                lock (_cacheLock)
                {
                    return _cache.ContainsKey(key);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // ITransactionalResource implementation
        public string ResourceId => $"FileStore:{_filePath}";

        public Task<bool> PrepareAsync(ITransaction transaction, CancellationToken cancellationToken = default)
        {
            // For file-based storage, we can prepare by ensuring the file is accessible
            return Task.FromResult(true);
        }

        public async Task CommitAsync(ITransaction transaction, CancellationToken cancellationToken = default)
        {
            // Ensure changes are persisted to file
            await SaveToFileAsync();
        }

        public Task RollbackAsync(ITransaction transaction, CancellationToken cancellationToken = default)
        {
            // Reload from file to discard in-memory changes
            LoadCache();
            return Task.CompletedTask;
        }

        public Task CreateSavepointAsync(ITransaction transaction, ISavepoint savepoint, CancellationToken cancellationToken = default)
        {
            // For simplicity, we don't implement savepoints in file store
            return Task.CompletedTask;
        }

        public Task RollbackToSavepointAsync(ITransaction transaction, ISavepoint savepoint, CancellationToken cancellationToken = default)
        {
            // For simplicity, we don't implement savepoints in file store
            return Task.CompletedTask;
        }

        public Task DiscardSavepointDataAsync(ITransaction transaction, ISavepoint savepointToDiscard, CancellationToken cancellationToken = default)
        {
            // For simplicity, we don't implement savepoints in file store
            return Task.CompletedTask;
        }
    }
}