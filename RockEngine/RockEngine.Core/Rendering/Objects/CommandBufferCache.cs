/*using NLog;

using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Vulkan;


using System.Collections.Concurrent;

namespace RockEngine.Core.Rendering.Objects
{
    public class CommandBufferCache : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly IRenderSubPass[] _subPasses;

        // Cache with proper lifetime management
        private readonly ConcurrentDictionary<CommandBufferCacheKey, (UploadBatch Batch, DateTime LastUsed)> _cache
            = new ConcurrentDictionary<CommandBufferCacheKey, (UploadBatch, DateTime)>();

        private readonly LinkedList<CommandBufferCacheKey> _lruList = new LinkedList<CommandBufferCacheKey>();
        private readonly object _lruLock = new object();

        private readonly int _maxCacheSize;
        private readonly TimeSpan _maxCacheAge = TimeSpan.FromSeconds(10); // Shorter timeout
        private DateTime _lastCleanup = DateTime.UtcNow;
        private bool _disposed = false;

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public CommandBufferCache(VulkanContext context, IEnumerable<IRenderSubPass> subPasses, int maxCacheSize = 50) // Reduced size
        {
            _context = context;
            _subPasses = subPasses.ToArray();
            _maxCacheSize = maxCacheSize;
        }

        public bool TryGet(CommandBufferCacheKey key, out UploadBatch? batch)
        {
            if (_disposed)
            {
                batch = null;
                return false;
            }

            if (_cache.TryGetValue(key, out var cached))
            {
                // Check if batch is still valid and not disposed
                if (cached.Batch?.CommandBuffer?.IsDisposed == false)
                {
                    // Update LRU and usage time
                    lock (_lruLock)
                    {
                        _lruList.Remove(key);
                        _lruList.AddFirst(key);
                    }

                    _cache[key] = (cached.Batch, DateTime.UtcNow);
                    batch = cached.Batch;
                    return true;
                }
                else
                {
                    // Remove disposed batch
                    _cache.TryRemove(key, out _);
                }
            }

            batch = null;
            return false;
        }

        public void Store(CommandBufferCacheKey key, UploadBatch batch)
        {
            if (_disposed)
            {
                batch.Dispose();
                return;
            }

            // Cleanup every store operation to prevent accumulation
            Cleanup();

            // Check cache size and evict if necessary
            while (_cache.Count >= _maxCacheSize)
            {
                if (!EvictLeastRecentlyUsed())
                    break; // Stop if we can't evict more
            }

            if (_cache.TryAdd(key, (batch, DateTime.UtcNow)))
            {
                lock (_lruLock)
                {
                    _lruList.AddFirst(key);
                }

                _logger.Trace($"Cached command buffer: {key}, Total: {_cache.Count}/{_maxCacheSize}");
            }
            else
            {
                // If we couldn't cache it, dispose immediately
                batch.Dispose();
            }
        }

        public void ClearFramebufferDependent()
        {
            var keysToRemove = _cache.Keys
                .Where(key => key.IsFramebufferDependent)
                .ToList();

            foreach (var key in keysToRemove)
            {
                if (_cache.TryRemove(key, out var cached))
                {
                    cached.Batch.Dispose();
                    lock (_lruLock)
                    {
                        _lruList.Remove(key);
                    }
                }
            }
        }

        private void Cleanup()
        {
            var now = DateTime.UtcNow;
            var keysToRemove = new List<CommandBufferCacheKey>();

            // Cleanup based on age and disposal status
            foreach (var kvp in _cache)
            {
                if (now - kvp.Value.LastUsed > _maxCacheAge ||
                    kvp.Value.Batch.CommandBuffer?.IsDisposed == true)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                if (_cache.TryRemove(key, out var cached))
                {
                    cached.Batch.Dispose();
                    lock (_lruLock)
                    {
                        _lruList.Remove(key);
                    }
                }
            }

            _lastCleanup = now;
        }

        private bool EvictLeastRecentlyUsed()
        {
            lock (_lruLock)
            {
                if (_lruList.Count == 0) return false;

                var lruKey = _lruList.Last?.Value;
                if (lruKey != null && _cache.TryRemove(lruKey.Value, out var cached))
                {
                    cached.Batch.Dispose();
                    _lruList.RemoveLast();
                    _logger.Trace($"Evicted LRU cache entry: {lruKey}");
                    return true;
                }
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            var batches = _cache.Values.Select(x => x.Batch).ToArray();
            _cache.Clear();

            lock (_lruLock)
            {
                _lruList.Clear();
            }

            foreach (var batch in batches)
            {
                batch?.Dispose();
            }

            _logger.Info("Command buffer cache disposed");
        }

        public CacheStatistics GetStatistics()
        {
            return new CacheStatistics
            {
                TotalEntries = _cache.Count,
                MaxSize = _maxCacheSize,
                EstimatedMemoryUsage = _maxCacheSize,
                Utilization = (double)_cache.Count / _maxCacheSize * 100.0
            };
        }
        public struct CacheStatistics
        {
            public int TotalEntries { get; set; }
            public int MaxSize { get; set; }
            public long EstimatedMemoryUsage { get; set; }
            public double Utilization { get; set; }
        }
    }

    public struct CommandBufferCacheKey : IEquatable<CommandBufferCacheKey>
    {
        public Type SubpassType { get; }
        public uint FrameIndex { get; }
        public int SubpassIndex { get; }
        public DateTime CreationTime { get; }
        public bool IsFramebufferDependent { get; set; }

        public CommandBufferCacheKey(Type subpassType, uint frameIndex, int subpassIndex, bool isFramebufferDependent = true)
        {
            SubpassType = subpassType;
            FrameIndex = frameIndex;
            SubpassIndex = subpassIndex;
            CreationTime = DateTime.UtcNow;
            IsFramebufferDependent = isFramebufferDependent;
        }

        public bool Equals(CommandBufferCacheKey other)
        {
            return SubpassType == other.SubpassType &&
                   FrameIndex == other.FrameIndex &&
                   SubpassIndex == other.SubpassIndex &&
                   IsFramebufferDependent == other.IsFramebufferDependent;
        }

        public override bool Equals(object? obj)
        {
            return obj is CommandBufferCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SubpassType, FrameIndex, SubpassIndex, IsFramebufferDependent);
        }

        public override string ToString()
        {
            return $"{SubpassType.Name}[Frame:{FrameIndex},Subpass:{SubpassIndex}]";
        }
        public static bool operator ==(CommandBufferCacheKey left, CommandBufferCacheKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(CommandBufferCacheKey left, CommandBufferCacheKey right)
        {
            return !(left == right);
        }
    }
}*/