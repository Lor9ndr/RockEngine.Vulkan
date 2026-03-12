
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using NLog;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;

namespace RockEngine.Core.Diagnostics
{
    public sealed class PerformanceTracer : IDisposable
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        public const uint QUERIES_PER_FRAME = 200;
        private const float MIN_DISPLAY_DURATION = 0.01f;
        private const double UPDATE_INTERVAL_SECONDS = 0.5;
        private const int MAX_SAMPLES = 300;
        private const int CLEANUP_INTERVAL = 300;
        private const int UNUSED_FRAME_THRESHOLD = 600;
        private static long _lastScopeId = 1000;

        // String interning and pooling
        private static readonly ConcurrentDictionary<string, string> _stringPool = new();
        private static readonly ConcurrentDictionary<StringKey, ScopeInfo> _scopesByPath = new();
        private static readonly WeakReference<ScopeInfo>[] _scopesById = new WeakReference<ScopeInfo>[10000];
        private static readonly ScopeData[] _scopeDataArray = new ScopeData[10000];
        private static readonly ConcurrentDictionary<int, List<int>> _childScopesByParentId = new();

        // Frame data
        private static PerFrameData[] _frameData = Array.Empty<PerFrameData>();
        private static float _timestampPeriod;
        private static int _maxFramesPerFlight;
        private static int _cleanupCounter;
        private static long _currentFrameCount;


        // Thread-local current scopes
        private static readonly AsyncLocal<ScopeInfo> _currentCpuScope = new();
        private static readonly AsyncLocal<ScopeInfo> _currentGpuScope = new();

        // Root scopes
        public static readonly ScopeInfo CpuRoot;
        public static readonly ScopeInfo GpuRoot;

        public static IReadOnlyDictionary<int, List<int>> ChildScopesByParentId => _childScopesByParentId;
        public static ArraySegment<ScopeData> ScopeDataArray => _scopeDataArray;
        public static bool GPUTimestampsSupported => _gpuTimestampsSupported;
        public static ArraySegment<WeakReference<ScopeInfo>> ScopesById => _scopesById;

        // Track if GPU timestamps are supported
        private static bool _gpuTimestampsSupported = false;


        // String interning helper
        private static string InternString(string str)
        {
            return _stringPool.GetOrAdd(str, str);
        }

        // Struct for efficient string comparison in dictionary
        private readonly struct StringKey : IEquatable<StringKey>
        {
            public readonly string Value;
            private readonly int _hashCode;

            public StringKey(string value)
            {
                Value = value;
                _hashCode = value?.GetHashCode() ?? 0;
            }

            public bool Equals(StringKey other)
            {
                return ReferenceEquals(Value, other.Value) || string.Equals(Value, other.Value);
            }

            public override bool Equals(object obj)
            {
                return obj is StringKey other && Equals(other);
            }

            public override int GetHashCode() => _hashCode;

            public static implicit operator StringKey(string value) => new(value);
        }

        static PerformanceTracer()
        {
            var cpuRootName = InternString("CPU");
            var gpuRootName = InternString("GPU");

            CpuRoot = new ScopeInfo(1, cpuRootName, null, cpuRootName);
            GpuRoot = new ScopeInfo(2, gpuRootName, null, gpuRootName);

            _scopesByPath[cpuRootName] = CpuRoot;
            _scopesByPath[gpuRootName] = GpuRoot;
            _scopesById[1] = new WeakReference<ScopeInfo>(CpuRoot);
            _scopesById[2] = new WeakReference<ScopeInfo>(GpuRoot);
            _scopeDataArray[1] = new ScopeData();
            _scopeDataArray[2] = new ScopeData();
            _childScopesByParentId[1] = new List<int>();
            _childScopesByParentId[2] = new List<int>();

            // Initialize thread locals
            _currentCpuScope.Value = CpuRoot;
            _currentGpuScope.Value = GpuRoot;
        }

        public static void Initialize(VulkanContext context)
        {
            // Check if GPU timestamps are supported
            var properties = context.Device.PhysicalDevice.Properties;
            _gpuTimestampsSupported = properties.Limits.TimestampComputeAndGraphics;

            if (!_gpuTimestampsSupported)
            {
                _logger.Warn("GPU timestamp queries are not supported on this device. GPU profiling will be disabled.");
                return;
            }

            _timestampPeriod = properties.Limits.TimestampPeriod;
            _maxFramesPerFlight = context.MaxFramesPerFlight;
            _frameData = new PerFrameData[_maxFramesPerFlight];
            _cleanupCounter = 0;

            for (int i = 0; i < _maxFramesPerFlight; i++)
            {
                var frameData = new PerFrameData(context);
                frameData.QueryPool.LabelObject($"PerformanceTracer.QueryPool({i})");
                _frameData[i] = frameData;
            }
        }

        public void Dispose()
        {
            foreach (var frame in _frameData)
            {
                frame?.Dispose();
            }
            _frameData = Array.Empty<PerFrameData>();
            GC.SuppressFinalize(this);
        }

        public static CpuSectionTracker BeginSection(string name)
        {
            return new CpuSectionTracker(name);
        }

        public static GpuSectionTracker BeginSection(string name, UploadBatch batch, uint frameIndex)
        {
            if (!_gpuTimestampsSupported)
            {
                return new GpuSectionTracker(); // Return a disabled tracker
            }
            var frame = _frameData[frameIndex];
            return new GpuSectionTracker(name, batch, frame);
        }

        public static void BeginFrame(uint frameIndex)
        {
            if (!_gpuTimestampsSupported)
            {
                return;
            }

            var frame = _frameData[frameIndex];
            frame.BeginFrame();
        }
      

        public static void ProcessQueries(VulkanContext context, uint frameIndex)
        {
            if (!_gpuTimestampsSupported)
            {
                return;
            }

            var frame = _frameData[frameIndex];

            // Ensure the frame has started and has queries to process
            if (!frame.FrameStarted || frame.QueryCount == 0)
            {
                return;
            }

            frame.Process(context);
            Interlocked.Increment(ref _currentFrameCount);

            if (++_cleanupCounter >= CLEANUP_INTERVAL)
            {
                _cleanupCounter = 0;
                CleanupUnusedScopes();
            }
        }

        public static VkQueryPool? GetQueryPool(uint frameIndex)
        {
            if (!_gpuTimestampsSupported)
            {
                return null;
            }

            var frame = _frameData[frameIndex];
            return frame.QueryPool;
        }

        private static void CleanupUnusedScopes()
        {
            long currentFrame = Interlocked.Read(ref _currentFrameCount);

            if (_scopesByPath.Count <= 100)
            {
                return;
            }

            foreach (var kvp in _scopesByPath)
            {
                int id = kvp.Value.Id;
                if (id == 1 || id == 2)
                {
                    continue; // Skip roots
                }

                var data = _scopeDataArray[id];
                if (data == null)
                {
                    continue;
                }

                if (currentFrame - data.LastUsedFrame > UNUSED_FRAME_THRESHOLD)
                {
                    RemoveScope(id);
                }
            }
        }

        private static void RemoveScope(int id)
        {
            if (!_scopesById[id].TryGetTarget(out var scope))
            {
                return;
            }

            _scopesByPath.TryRemove(scope.FullPath, out _);
            _scopesById[id] = null;
            _scopeDataArray[id] = null;

            if (scope.ParentId.HasValue &&
                _childScopesByParentId.TryGetValue(scope.ParentId.Value, out var parentChildren))
            {
                lock (parentChildren)
                {
                    parentChildren.Remove(id);
                }
            }
        }

        private static ScopeInfo GetOrCreateScope(string name, ScopeInfo parent)
        {
            // Intern the name first
            string internedName = InternString(name);

            string fullPath;
            if (parent != null)
            {
                // Use StringBuilder for path construction to avoid intermediate strings
                var sb = new StringBuilder(parent.FullPath.Length + internedName.Length + 1);
                sb.Append(parent.FullPath);
                sb.Append('/');
                sb.Append(internedName);
                fullPath = InternString(sb.ToString());
            }
            else
            {
                fullPath = internedName;
            }

            // Check if scope already exists using our optimized StringKey
            var key = new StringKey(fullPath);
            if (_scopesByPath.TryGetValue(key, out var existing))
            {
                return existing;
            }

            // Create new scope with interned strings
            int newId = (int)Interlocked.Increment(ref _lastScopeId);
            var newScope = new ScopeInfo(newId, internedName, parent?.Id, fullPath);

            // Add to dictionaries
            _scopesByPath[key] = newScope;
            _scopesById[newId] = new WeakReference<ScopeInfo>(newScope);
            _scopeDataArray[newId] = new ScopeData();

            // Add to parent's children
            if (parent != null)
            {
                var parentChildren = _childScopesByParentId.GetOrAdd(parent.Id, _ => new List<int>());
                lock (parentChildren)
                {
                    parentChildren.Add(newId);
                }
            }

            return newScope;
        }

        private struct ValueStopwatch
        {
            private long _startTimestamp;
            public readonly bool IsActive => _startTimestamp != 0;

            public static ValueStopwatch StartNew() => new()
            {
                _startTimestamp = Stopwatch.GetTimestamp()
            };

            public readonly TimeSpan Elapsed
            {
                get
                {
                    long end = Stopwatch.GetTimestamp();
                    long ticks = (end - _startTimestamp) * TimeSpan.TicksPerSecond / Stopwatch.Frequency;
                    return new TimeSpan(ticks);
                }
            }
        }

        public sealed class ScopeInfo
        {
            public int Id { get; }
            public string Name { get; }
            public int? ParentId { get; }
            public string FullPath { get; }

            public ScopeInfo(int id, string name, int? parentId, string fullPath)
            {
                Id = id;
                Name = name;
                ParentId = parentId;
                FullPath = fullPath;
            }
        }

        public sealed class ScopeData
        {
            public float LastDuration;
            public float AverageDuration;
            public float MaxDuration;
            public float MinDuration = float.MaxValue;
            public int SampleCount;
            public long LastUsedFrame;

            public void AddDuration(float duration, long frameCount)
            {
                LastDuration = duration;
                LastUsedFrame = frameCount;

                if (duration < MinDuration)
                {
                    MinDuration = duration;
                }

                if (duration > MaxDuration)
                {
                    MaxDuration = duration;
                }

                if (SampleCount < MAX_SAMPLES)
                {
                    AverageDuration = (AverageDuration * SampleCount + duration) / (SampleCount + 1);
                    SampleCount++;
                }
                else
                {
                    // Exponential moving average
                    const float alpha = 0.1f;
                    AverageDuration = AverageDuration * (1 - alpha) + duration * alpha;
                }
            }
        }

        public struct CpuSectionTracker : IDisposable
        {
            private readonly int _scopeId;
            private readonly ValueStopwatch _sw;
            private readonly ScopeInfo _previousScope;
            private bool _disposed;

            public CpuSectionTracker(string name)
            {
                _previousScope = _currentCpuScope.Value;
                var parent = _previousScope ?? CpuRoot;
                var scopeInfo = GetOrCreateScope(name, parent);
                _scopeId = scopeInfo.Id;
                _sw = ValueStopwatch.StartNew();
                _currentCpuScope.Value = scopeInfo;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                float duration = (float)_sw.Elapsed.TotalMilliseconds;
                long frameCount = Interlocked.Read(ref _currentFrameCount);

                var data = _scopeDataArray[_scopeId];
                data?.AddDuration(duration, frameCount);

                _currentCpuScope.Value = _previousScope;
            }
        }

        public struct GpuSectionTracker : IDisposable
        {
            private readonly int _scopeId;
            private readonly UploadBatch _batch;
            private readonly PerFrameData _frame;
            private readonly uint _startIndex;
            private readonly bool _valid;
            private readonly ScopeInfo _previousScope;
            private bool _disposed;

            public GpuSectionTracker() // Disabled constructor
            {
                _valid = false;
                _scopeId = 0;
                _batch = null;
                _frame = null;
                _startIndex = 0;
                _previousScope = null;
                _disposed = false;
            }

            public GpuSectionTracker(string name, UploadBatch batch, PerFrameData frame)
            {
                _previousScope = _currentGpuScope.Value;
                var parent = _previousScope ?? GpuRoot;
                var scopeInfo = GetOrCreateScope(name, parent);
                _scopeId = scopeInfo.Id;
                _batch = batch;
                _frame = frame;
                _valid = true;
                _disposed = false;

                // Reserve queries first to get the start index
                _startIndex = _frame.ReserveQueries(_scopeId, 2); 
                // resetting query pools by cmd, not by host.
                _batch.ResetQueryPool(_frame.QueryPool, _startIndex, 2);
                // Write start timestamp
                _batch.WriteTimestamp(PipelineStageFlags2.AllCommandsBit, _frame.QueryPool, _startIndex);

                _currentGpuScope.Value = scopeInfo;
            }

            public void Dispose()
            {
                if (_disposed || !_valid)
                {
                    return;
                }

                _disposed = true;

                // Write end timestamp
                _batch.WriteTimestamp(PipelineStageFlags2.AllCommandsBit, _frame.QueryPool, _startIndex + 1);
                
                _currentGpuScope.Value = _previousScope;
            }
           
        }

        public sealed class PerFrameData : IDisposable
        {
            private readonly VulkanContext _context;
            private readonly List<GpuQuery> _queries = new();
            private uint _nextQueryIndex = 0;
            private bool _frameStarted = false;

            public bool FrameStarted => _frameStarted;
            public uint QueryCount => _nextQueryIndex;
            public VkQueryPool QueryPool { get; private set; }
            private readonly Lock _queryLock = new Lock();

            public PerFrameData(VulkanContext context)
            {
                _context = context;

                if (!_gpuTimestampsSupported)
                {
                    return;
                }

                var createInfo = new QueryPoolCreateInfo
                {
                    SType = StructureType.QueryPoolCreateInfo,
                    QueryType = QueryType.Timestamp,
                    QueryCount = QUERIES_PER_FRAME
                };

                QueryPool = VkQueryPool.Create(context, createInfo);
            }

            internal void ResetQueryPool(UploadBatch currentBatch)
            {
                Console.WriteLine();
                currentBatch.ResetQueryPool(QueryPool, 0, _nextQueryIndex);
                lock (_queryLock)
                {
                    _nextQueryIndex = 0;
                    _queries.Clear();
                }
            }

            public void Dispose()
            {
                QueryPool?.Dispose();
            }

            public void BeginFrame()
            {
                if (!_gpuTimestampsSupported)
                {
                    return;
                }

                _nextQueryIndex = 0;
                _queries.Clear();
                _frameStarted = true;
            }

            public unsafe void Process(VulkanContext context)
            {
                if (!_gpuTimestampsSupported || _queries.Count == 0 || !_frameStarted)
                {
                    _frameStarted = false;
                    return;
                }

                var vk = VulkanContext.Vk;
                uint queryCount = _nextQueryIndex;

                // Allocate space for (timestamp, availability) pairs
                ulong* timestampsAndAvailability = stackalloc ulong[(int)queryCount * 2];
                nuint dataSize = queryCount * sizeof(ulong) * 2; // Total size in bytes
                nuint stride = sizeof(ulong) * 2; // Stride between query results (16 bytes)

                {
                    var result = vk.GetQueryPoolResults(
                        context.Device,
                        QueryPool.VkObjectNative,
                        0,
                        queryCount,
                        dataSize,
                        timestampsAndAvailability,
                        stride,
                        QueryResultFlags.Result64Bit | QueryResultFlags.ResultWithAvailabilityBit
                    );

                    if (result != Result.Success)
                    {
                        if (result == Result.NotReady)
                        {
                            // Results aren't ready yet, try again next frame
                        }
                        else
                        {
                            _logger.Warn($"Failed to read query pool results: {result}");
                        }

                        _frameStarted = false;
                        return;
                    }
                }

                // Process each query pair
                foreach (var query in _queries)
                {
                    if (query.StartIndex + 1 >= queryCount)
                    {
                        continue;
                    }

                    // Each query takes 2 slots: [timestamp, availability]
                    int startBaseIdx = (int)query.StartIndex * 2;
                    int endBaseIdx = (int)(query.StartIndex + 1) * 2;

                    // Get timestamp and availability for start query
                    ulong startTime = timestampsAndAvailability[startBaseIdx];
                    ulong startTimeAvailability = timestampsAndAvailability[startBaseIdx + 1];

                    // Get timestamp and availability for end query
                    ulong endTime = timestampsAndAvailability[endBaseIdx];
                    ulong endTimeAvailability = timestampsAndAvailability[endBaseIdx + 1];

                    // Check if both results are available (availability != 0)
                    if (startTimeAvailability == 0 || endTimeAvailability == 0)
                    {
                        continue; // Skip this query pair if results aren't ready
                    }

                    // Sanity check for valid timestamps (though availability check is primary)
                    if (startTime == 0 || endTime == 0)
                    {
                        continue;
                    }

                    // Ensure end time is greater than start time (should be for valid queries)
                    if (endTime <= startTime)
                    {
                        continue;
                    }

                    // Convert to milliseconds
                    float duration = (endTime - startTime) * _timestampPeriod * 1e-6f;
                    long frameCount = Interlocked.Read(ref _currentFrameCount);

                    var data = _scopeDataArray[query.ScopeId];
                    data?.AddDuration(duration, frameCount);
                }

                _frameStarted = false;
            }

            public uint ReserveQueries(int scopeId, uint count)
            {
                if (!_frameStarted)
                {
                    _logger.Warn("Attempted to reserve queries before frame started");
                    return 0;
                }

                if (_nextQueryIndex + count > QUERIES_PER_FRAME)
                {
                    _logger.Warn($"Query pool overflow - too many GPU scopes in one frame. Requested {count}, available {QUERIES_PER_FRAME - _nextQueryIndex}");
                    return 0;
                }

                lock (_queryLock)
                {
                    if (!_frameStarted || _nextQueryIndex + count > QUERIES_PER_FRAME)
                    {
                        return 0;
                    }

                    uint startIndex = _nextQueryIndex;
                    _queries.Add(new GpuQuery(scopeId, (int)startIndex));
                    _nextQueryIndex += count;
                    return startIndex;
                }
            }

          

            private readonly struct GpuQuery
            {
                public readonly int ScopeId;
                public readonly int StartIndex;

                public GpuQuery(int scopeId, int startIndex)
                {
                    ScopeId = scopeId;
                    StartIndex = startIndex;
                }
            }
        }
    }
}