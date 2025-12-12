//using ImGuiNET;

using NLog;

using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Text;

namespace RockEngine.Core
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

        // UI state
        private static readonly bool[] _expandedNodes = new bool[10000];
        private static string _searchFilter = string.Empty;
        private static bool _showOnlySignificant = true;
        private static float _minDurationFilter = MIN_DISPLAY_DURATION;
        private static long _lastFrameCount;
        private static double _fps;
        private static readonly Stopwatch _updateTimer = Stopwatch.StartNew();

        // Thread-local current scopes
        private static readonly AsyncLocal<ScopeInfo> _currentCpuScope = new();
        private static readonly AsyncLocal<ScopeInfo> _currentGpuScope = new();

        // Root scopes
        private static readonly ScopeInfo _cpuRoot;
        private static readonly ScopeInfo _gpuRoot;

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

            _cpuRoot = new ScopeInfo(1, cpuRootName, null, cpuRootName);
            _gpuRoot = new ScopeInfo(2, gpuRootName, null, gpuRootName);

            _scopesByPath[cpuRootName] = _cpuRoot;
            _scopesByPath[gpuRootName] = _gpuRoot;
            _scopesById[1] = new WeakReference<ScopeInfo>(_cpuRoot);
            _scopesById[2] = new WeakReference<ScopeInfo>(_gpuRoot);
            _scopeDataArray[1] = new ScopeData();
            _scopeDataArray[2] = new ScopeData();
            _childScopesByParentId[1] = new List<int>();
            _childScopesByParentId[2] = new List<int>();
            _expandedNodes[1] = true;
            _expandedNodes[2] = true;

            // Initialize thread locals
            _currentCpuScope.Value = _cpuRoot;
            _currentGpuScope.Value = _gpuRoot;
        }

        public static void Initialize(VulkanContext context)
        {
            // Check if GPU timestamps are supported
            var properties = context.Device.PhysicalDevice.Properties;
            _gpuTimestampsSupported = properties.Limits.TimestampComputeAndGraphics;

            if (!_gpuTimestampsSupported)
            {
                _logger.Warn("GPU timestamp queries are not supported on this device. GPU profiling will be disabled.");
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
            // Reset the query pool immediately
            frame.ResetQueryPool();
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

            var frame = _frameData[frameIndex % _maxFramesPerFlight];
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

       /* public static void DrawMetrics()
        {
            double elapsedSeconds = _updateTimer.Elapsed.TotalSeconds;
            if (elapsedSeconds > UPDATE_INTERVAL_SECONDS)
            {
                long currentFrame = Interlocked.Read(ref _currentFrameCount);
                _fps = (currentFrame - _lastFrameCount) / elapsedSeconds;
                _lastFrameCount = currentFrame;
                _updateTimer.Restart();
            }

            ImGui.Text($"FPS: {_fps:0.0}");

            if (!_gpuTimestampsSupported)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "GPU Timestamps Not Supported");
            }

            ImGui.Checkbox("Only Significant", ref _showOnlySignificant);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            ImGui.DragFloat("Min Duration (ms)", ref _minDurationFilter, 0.01f, 0.01f, 100f);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200);
            ImGui.InputText("Search", ref _searchFilter, 100);

            if (ImGui.CollapsingHeader("CPU Timings", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawChildScopes(_cpuRoot.Id);
            }

            if (ImGui.CollapsingHeader("GPU Timings", ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (_gpuTimestampsSupported)
                {
                    DrawChildScopes(_gpuRoot.Id);
                }
                else
                {
                    ImGui.Text("GPU timestamps not supported on this device");
                }
            }
        }

        private static void DrawChildScopes(int parentId)
        {
            if (!_childScopesByParentId.TryGetValue(parentId, out var children))
            {
                return;
            }

            lock (children)
            {
                foreach (int childId in children)
                {
                    var scopeID = _scopesById[childId];
                    if (scopeID != null && scopeID.TryGetTarget(out var scope))
                    {
                        var data = _scopeDataArray[childId];
                        if (data != null)
                        {
                            DrawScopeNode(scope, data);
                        }
                    }
                }
            }
        }

        private static void DrawScopeNode(ScopeInfo scope, ScopeData data)
        {
            bool hasChildren = _childScopesByParentId.TryGetValue(scope.Id, out var children) &&
                               children.Count > 0;

            // Filtering
            bool shouldShow = !_showOnlySignificant || data.AverageDuration >= _minDurationFilter || hasChildren;
            shouldShow = shouldShow && (string.IsNullOrEmpty(_searchFilter) ||
                         scope.FullPath.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase));

            if (!shouldShow)
            {
                return;
            }

            float displayDuration = data.LastDuration > 0 ? data.LastDuration : data.AverageDuration;
            float fraction = Math.Clamp(displayDuration / 33f, 0f, 1f);

            ImGui.ProgressBar(fraction, new Vector2(100, 20), $"{displayDuration:0.00}ms");
            ImGui.SameLine();

            bool isExpanded = _expandedNodes[scope.Id];
            ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.OpenOnArrow;
            if (!hasChildren)
            {
                flags |= ImGuiTreeNodeFlags.Leaf;
            }

            bool nodeOpen = ImGui.TreeNodeEx($"{scope.Name}##{scope.Id}", flags);

            if (ImGui.IsItemClicked())
            {
                _expandedNodes[scope.Id] = !isExpanded;
            }

            if (nodeOpen)
            {
                if (hasChildren)
                {
                    ImGui.Indent(10);
                    lock (children)
                    {
                        foreach (int childId in children)
                        {
                            var scopeID = _scopesById[childId];
                            if (scopeID != null && scopeID.TryGetTarget(out var childScope))
                            {
                                var childScopeData = _scopeDataArray[childId];
                                if (childScopeData != null)
                                {
                                    DrawScopeNode(childScope, childScopeData);
                                }
                            }
                        }
                    }
                    ImGui.Unindent(10);
                }
                ImGui.TreePop();
            }

            // Tooltip with detailed information
            if (ImGui.IsItemHovered())
            {
                *//*//ImGui.BeginTooltip();
                ImGui.Text($"Full Path: {scope.FullPath}");
                ImGui.Text($"Average: {data.AverageDuration:0.00}ms");
                ImGui.Text($"Last: {data.LastDuration:0.00}ms");
                ImGui.Text($"Min: {data.MinDuration:0.00}ms");
                ImGui.Text($"Max: {data.MaxDuration:0.00}ms");
                ImGui.Text($"Samples: {data.SampleCount}");
                //ImGui.EndTooltip();*//*
            }
        }*/

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

        private sealed class ScopeInfo
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

        private sealed class ScopeData
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
                var parent = _previousScope ?? _cpuRoot;
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
                var parent = _previousScope ?? _gpuRoot;
                var scopeInfo = GetOrCreateScope(name, parent);
                _scopeId = scopeInfo.Id;
                _batch = batch;
                _frame = frame;
                _valid = true;
                _disposed = false;

                // Reserve queries first to get the start index
                _startIndex = frame.ReserveQueries(_scopeId, 2);

                // Write start timestamp
                batch.WriteTimestamp(PipelineStageFlags.BottomOfPipeBit, frame.QueryPool, _startIndex);

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
                _batch.WriteTimestamp(PipelineStageFlags.BottomOfPipeBit, _frame.QueryPool, _startIndex + 1);
                
                _currentGpuScope.Value = _previousScope;
            }
        }

        public sealed class PerFrameData : IDisposable
        {
            private readonly VulkanContext _context;
            private readonly List<GpuQuery> _queries = new();
            private uint _nextQueryIndex = 0;
            private bool _frameStarted = false;
            private bool _queriesUsedInFrame = false; // Track if queries were used

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

                // Reset the entire query pool initially
                ResetQueryPool();
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

                // Reset query pool at the beginning of the frame if queries were used in previous frame
                if (_queriesUsedInFrame)
                {
                    ResetQueryPool();
                }

                _nextQueryIndex = 0;
                _queries.Clear();
                _frameStarted = true;
                _queriesUsedInFrame = false; // Reset for this frame
            }

            public void ResetQueryPool()
            {
                if (QueryPool != null && QueryPool.VkObjectNative.Handle != 0)
                {
                    var vk = VulkanContext.Vk;
                    unsafe
                    {
                        // Use vkCmdResetQueryPool for better performance (reset in command buffer)
                        // Or use vkResetQueryPool for host reset
                        vk.ResetQueryPool(
                            _context.Device,
                            QueryPool.VkObjectNative,
                            0,
                            QUERIES_PER_FRAME
                        );
                    }
                }
            }

            public unsafe void Process(VulkanContext context)
            {
                if (!_gpuTimestampsSupported || _queries.Count == 0 || !_frameStarted)
                {
                    _frameStarted = false;
                    return;
                }

                var vk = VulkanContext.Vk;

                // Read timestamp results
                var timestamps = stackalloc ulong[(int)_nextQueryIndex];
                {
                    var result = vk.GetQueryPoolResults(
                        context.Device,
                        QueryPool.VkObjectNative,
                        0,
                        _nextQueryIndex,
                        _nextQueryIndex * sizeof(ulong),
                        timestamps,
                        sizeof(ulong),
                        QueryResultFlags.Result64Bit | QueryResultFlags.ResultWaitBit
                    );

                    if (result != Result.Success)
                    {
                        _logger.Warn($"Failed to read query pool results: {result}");
                        _frameStarted = false;
                        return;
                    }
                }

                // Process each query pair
                foreach (var query in _queries)
                {
                    if (query.StartIndex + 1 >= _nextQueryIndex)
                    {
                        continue;
                    }

                    ulong startTime = timestamps[query.StartIndex];
                    ulong endTime = timestamps[query.StartIndex + 1];

                    if (startTime == 0 || endTime == 0)
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
                // Don't reset here - reset will happen in BeginFrame of next frame
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
                    _queriesUsedInFrame = true; // Mark that queries were used this frame
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