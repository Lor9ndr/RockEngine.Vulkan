using ImGuiNET;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;
using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Collections.Concurrent;
using NLog;
using System.Threading;

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

        // Thread-safe scope tracking with object pooling
        private static readonly ConcurrentDictionary<string, ScopeInfo> _scopesByPath = new();
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

        static PerformanceTracer()
        {
            _cpuRoot = new ScopeInfo(1, "CPU", null, "CPU");
            _gpuRoot = new ScopeInfo(2, "GPU", null, "GPU");

            _scopesByPath["CPU"] = _cpuRoot;
            _scopesByPath["GPU"] = _gpuRoot;
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
                _frameData[i] = new PerFrameData(context);
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

        public static GpuSectionTracker BeginSection(string name, VkCommandBuffer cmd, uint frameIndex)
        {
            if (!_gpuTimestampsSupported)
            {
                return new GpuSectionTracker(); // Return a disabled tracker
            }

            var frame = _frameData[frameIndex % _maxFramesPerFlight];

            return new GpuSectionTracker(name, cmd, frame);
        }

        public static void ProcessQueries(VulkanContext context, uint frameIndex)
        {
            if (!_gpuTimestampsSupported) return;

            var frame = _frameData[frameIndex % _maxFramesPerFlight];
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
            if (!_gpuTimestampsSupported) return null;
            var frame = _frameData[frameIndex % _maxFramesPerFlight];
            return frame.QueryPool;
        }


        private static void CleanupUnusedScopes()
        {
            long currentFrame = Interlocked.Read(ref _currentFrameCount);

            // Only clean up when we have many scopes to avoid unnecessary work
            if (_scopesByPath.Count <= 100) return;

            foreach (var kvp in _scopesByPath)
            {
                int id = kvp.Value.Id;
                if (id == 1 || id == 2) continue; // Skip roots

                var data = _scopeDataArray[id];
                if (data == null) continue;

                if (currentFrame - data.LastUsedFrame > UNUSED_FRAME_THRESHOLD)
                {
                    RemoveScope(id);
                }
            }
        }

        private static void RemoveScope(int id)
        {
            if (!_scopesById[id].TryGetTarget(out var scope)) return;

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

        public static void DrawMetrics()
        {
            if (!ImGui.Begin("Performance Metrics##PerformanceMetrics"))
            {
                ImGui.End();
                return;
            }

            try
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
            finally
            {
                ImGui.End();
            }
        }

        private static void DrawChildScopes(int parentId)
        {
            if (!_childScopesByParentId.TryGetValue(parentId, out var children))
                return;

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
            bool shouldShow = !_showOnlySignificant  || hasChildren;
            shouldShow = shouldShow && (data.AverageDuration >= _minDurationFilter || hasChildren);
            shouldShow = shouldShow && (!string.IsNullOrEmpty(_searchFilter) ||
                         scope.FullPath.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase));

            if (!shouldShow)
                return;

            float displayDuration = data.LastDuration > 0 ? data.LastDuration : data.AverageDuration;
            float fraction = Math.Clamp(displayDuration / 33f, 0f, 1f);

            ImGui.ProgressBar(fraction, new Vector2(100, 20), $"{displayDuration:0.00}ms");
            ImGui.SameLine();

            bool isExpanded = _expandedNodes[scope.Id];
            ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.OpenOnArrow;
            if (isExpanded) flags |= ImGuiTreeNodeFlags.DefaultOpen;

            bool nodeOpen = ImGui.TreeNodeEx($"{scope.Name}##{scope.Id}", flags);

            if (ImGui.IsItemClicked())
            {
                _expandedNodes[scope.Id] = !isExpanded;
            }

            if (nodeOpen)
            {
                ImGui.Indent(10);
                ImGui.Text($"Full Path: {scope.FullPath}");
                ImGui.Text($"Average: {data.AverageDuration:0.00}ms");
                ImGui.Text($"Last: {data.LastDuration:0.00}ms");
                ImGui.Text($"Min: {data.MinDuration:0.00}ms");
                ImGui.Text($"Max: {data.MaxDuration:0.00}ms");
                ImGui.Text($"Samples: {data.SampleCount}");

                // Draw children
                if (hasChildren)
                {
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
                }

                ImGui.Unindent(10);
                ImGui.TreePop();
            }
        }

        private static ScopeInfo GetOrCreateScope(string name, ScopeInfo parent)
        {
            string fullPath = parent != null ? $"{parent.FullPath}/{name}" : name;

            // Check if scope already exists
            if (_scopesByPath.TryGetValue(fullPath, out var existing))
            {
                return existing;
            }

            // Create new scope
            int newId = (int)Interlocked.Increment(ref _lastScopeId);
            var newScope = new ScopeInfo(newId, name, parent?.Id, fullPath);

            // Add to dictionaries
            _scopesByPath[fullPath] = newScope;
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

        public static void BeginFrame(VkCommandBuffer cmd, uint frameIndex)
        {
            if (!_gpuTimestampsSupported) return;

            var frame = _frameData[frameIndex % _maxFramesPerFlight];
            frame.BeginFrame(cmd);
        }

        private static long _lastScopeId = 1000;

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
           // public bool IsSignificant => AverageDuration >= MIN_DISPLAY_DURATION;

            public void AddDuration(float duration, long frameCount)
            {
                LastDuration = duration;
                LastUsedFrame = frameCount;

                if (duration < MinDuration) MinDuration = duration;
                if (duration > MaxDuration) MaxDuration = duration;

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
                _scopeId = scopeInfo.Id; // Fixed: was using undefined variable 'scopeId'
                _sw = ValueStopwatch.StartNew();
                _currentCpuScope.Value = scopeInfo;
            }

            public void Dispose()
            {
                if (_disposed) return;
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
            private readonly VkCommandBuffer _cmd;
            private readonly PerFrameData _frame;
            private readonly uint _startIndex;
            private readonly bool _valid;
            private readonly ScopeInfo _previousScope;
            private bool _disposed;

            public GpuSectionTracker() : this(null, null, null) // Disabled constructor
            {
                _valid = false;
            }

            public GpuSectionTracker(string name, VkCommandBuffer cmd, PerFrameData frame)
            {
                _previousScope = _currentGpuScope.Value;
                var parent = _previousScope ?? _gpuRoot;
                var scopeInfo = GetOrCreateScope(name, parent); 
                _scopeId = scopeInfo.Id;
                _cmd = cmd;
                _frame = frame;

                if (frame != null)
                {
                    _startIndex = _frame.ReserveQueries(_scopeId, 2);
                    _valid = _startIndex != uint.MaxValue;
                }
                else
                {
                    _startIndex = uint.MaxValue;
                    _valid = false;
                }

                _currentGpuScope.Value = scopeInfo;
                if (_valid)
                {
                    _cmd.WriteTimestamp(
                        PipelineStageFlags.TopOfPipeBit,
                        _frame.QueryPool,
                        _startIndex
                    );
                }
            }

            public void Dispose()
            {
                if (_disposed || !_valid) return;
                _disposed = true;

                _cmd.WriteTimestamp(
                    PipelineStageFlags.BottomOfPipeBit,
                    _frame.QueryPool,
                    _startIndex + 1
                );

                _currentGpuScope.Value = _previousScope;
            }
        }

        public sealed class PerFrameData : IDisposable
        {
            public VkQueryPool QueryPool { get; private set; }
            private readonly GpuQuery[] _pendingQueries;
            private int _pendingQueryCount;
            private uint _queryIndex;
            private bool _requiresReset;

            public PerFrameData(VulkanContext context)
            {
                var createInfo = new QueryPoolCreateInfo
                {
                    SType = StructureType.QueryPoolCreateInfo,
                    QueryType = QueryType.Timestamp,
                    QueryCount = QUERIES_PER_FRAME
                };

                QueryPool = VkQueryPool.Create(context, createInfo);
                _pendingQueries = new GpuQuery[QUERIES_PER_FRAME / 2];

                // Initial reset after creation
                QueryPool.Reset(0, QUERIES_PER_FRAME);
                _requiresReset = false;
            }

            public void Dispose()
            {
                QueryPool?.Dispose();
                QueryPool = null!;
            }

            public void BeginFrame(VkCommandBuffer cmd)
            {
                // Use device-side reset for better synchronization
                cmd.ResetQueryPool(QueryPool, 0, QUERIES_PER_FRAME);
                _queryIndex = 0;
                _pendingQueryCount = 0;
                _requiresReset = false;
            }

            public unsafe void Process(VulkanContext context)
            {
                if (_queryIndex == 0 || _pendingQueryCount == 0) return;

                Span<ulong> results = stackalloc ulong[(int)_queryIndex];
                try
                {
                    var status = QueryPool.GetResults(
                        firstQuery: 0,
                        queryCount: _queryIndex,
                        results,
                        stride: sizeof(ulong),
                        flags: QueryResultFlags.Result64Bit | QueryResultFlags.ResultWaitBit
                    );

                    if (status != Result.Success)
                    {
                        _logger.Warn($"Failed to get query results: {status}");
                        return;
                    }

                    long frameCount = Interlocked.Read(ref _currentFrameCount);
                    for (int i = 0; i < _pendingQueryCount; i++)
                    {
                        var query = _pendingQueries[i];
                        if (query.StartIndex + 1 >= _queryIndex) continue;

                        ulong start = results[query.StartIndex];
                        ulong end = results[query.StartIndex + 1];

                        if (end < start) continue;

                        float duration = (end - start) * (_timestampPeriod / 1e6f);
                        var data = _scopeDataArray[query.ScopeId];
                        data?.AddDuration(duration, frameCount);
                    }
                }
                finally
                {
                    _requiresReset = true;
                }
            }

            public uint ReserveQueries(int scopeId, uint count)
            {
                if (_queryIndex + count > QUERIES_PER_FRAME)
                {
                    return uint.MaxValue;
                }

                uint start = _queryIndex;
                _queryIndex += count;

                if (_pendingQueryCount < _pendingQueries.Length)
                {
                    _pendingQueries[_pendingQueryCount++] = new GpuQuery(scopeId, (int)start);
                }
                return start;
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