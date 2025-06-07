using ImGuiNET;

using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Diagnostics;

namespace RockEngine.Core
{
    public  class PerformanceTracer
    {
        private const uint QUERIES_PER_FRAME = 100;
        private const int MAX_HISTORY = 100;

        // CPU tracking
        private static readonly Dictionary<string, Queue<float>> _cpuDurations = new();
        private static readonly Dictionary<string, Stopwatch> _activeCpuTimers = new();

        // GPU tracking
        private static readonly Dictionary<string, Queue<float>> _gpuDurations = new();
        private static PerFrameData[] _frameData;
        private static float _timestampPeriod;
        private static int _maxFramesPerFlight;

        public static void Initialize(VulkanContext context)
        {
            _timestampPeriod = context.Device.PhysicalDevice.Properties.Limits.TimestampPeriod;
            _maxFramesPerFlight = context.MaxFramesPerFlight;
            _frameData = new PerFrameData[_maxFramesPerFlight];
            for (int i = 0; i < context.MaxFramesPerFlight; i++)
            {
                _frameData[i] = new PerFrameData(context);
            }
        }

        public static IDisposable BeginSection(string name)
        {
            return new CPUSectionTracker(name);
        }
        public static IDisposable BeginSection(string name, VkCommandBuffer cmd, int frameIndex)
        {
            var frame = _frameData[frameIndex % _maxFramesPerFlight];
            return new GpuSectionTracker(name, cmd, frame);
        }

        public static void ProcessQueries(VulkanContext context, int frameIndex)
        {
            var frame = _frameData[frameIndex % _maxFramesPerFlight];
            frame.Process(context);
        }


        public static void DrawMetrics()
        {
            if (ImGui.Begin("Performance Metrics"))
            {
                // CPU metrics
                ImGui.Text("CPU Timings");
                lock (_cpuDurations)
                {
                    foreach (var (name, values) in _cpuDurations)
                    {
                        var arr = values.ToArray();
                        ImGui.PlotLines(name, ref arr[0], arr.Length, 0, string.Empty, 0, 16.67f);
                    }
                }

                // GPU metrics
                ImGui.Text("GPU Timings");
                lock (_gpuDurations)
                {
                    foreach (var (name, values) in _gpuDurations)
                    {
                        var arr = values.ToArray();
                        ImGui.PlotLines(name, ref arr[0], arr.Length, 0, string.Empty, 0, 16.67f);
                    }
                }
                ImGui.End();
            }
        }

        private readonly struct CPUSectionTracker : IDisposable
        {
            private readonly string _name;
            private readonly Stopwatch _sw;

            public CPUSectionTracker(string name)
            {
                _name = name;
                _sw = Stopwatch.StartNew();
                lock (_activeCpuTimers) _activeCpuTimers[name] = _sw;
            }

            public void Dispose()
            {
                _sw.Stop();
                lock (_cpuDurations)
                {
                    if (!_cpuDurations.TryGetValue(_name, out var queue))
                    {
                        queue = new Queue<float>(MAX_HISTORY);
                        _cpuDurations[_name] = queue;
                    }
                    if (queue.Count >= MAX_HISTORY) queue.Dequeue();
                    queue.Enqueue((float)_sw.Elapsed.TotalMilliseconds);
                }
                lock (_activeCpuTimers) _activeCpuTimers.Remove(_name);
            }
        }
        private class PerFrameData
        {
            public readonly VkQueryPool QueryPool;
            private readonly List<GpuQuery> _pendingQueries = new();
            private uint _queryIndex;

            public PerFrameData(VulkanContext context)
            {
                var createInfo = new QueryPoolCreateInfo
                {
                    SType = StructureType.QueryPoolCreateInfo,
                    QueryType = QueryType.Timestamp,
                    QueryCount = QUERIES_PER_FRAME
                };
                QueryPool = VkQueryPool.Create(context, createInfo);
                QueryPool.LabelObject($"PerfQueryPool_{GetHashCode()}");
                QueryPool.Reset(0, QUERIES_PER_FRAME);
            }

            public unsafe void Process(VulkanContext context)
            {
                // Only process if we have queries
                if (_queryIndex > 0)
                {
                    QueryPool.Reset(0, QUERIES_PER_FRAME);

                    var results = stackalloc ulong[(int)_queryIndex];
                    unsafe
                    {
                        VulkanContext.Vk.GetQueryPoolResults(
                            context.Device,
                            QueryPool,
                            0,
                            _queryIndex,
                            sizeof(ulong) * _queryIndex,
                            results,
                            sizeof(ulong),
                            QueryResultFlags.None
                        );
                    }

                    lock (_gpuDurations)
                    {
                        foreach (var query in _pendingQueries)
                        {
                            if (query.StartIndex + 1 >= _queryIndex) continue;

                            var start = results[query.StartIndex];
                            var end = results[query.StartIndex + 1];
                            var duration = (end - start) * _timestampPeriod / 1e6f;

                            if (!_gpuDurations.TryGetValue(query.Name, out var queue))
                            {
                                queue = new Queue<float>(MAX_HISTORY);
                                _gpuDurations[query.Name] = queue;
                            }

                            if (queue.Count >= MAX_HISTORY) queue.Dequeue();
                            queue.Enqueue((float)duration);
                        }
                    }
                }

                _pendingQueries.Clear();
                _queryIndex = 0;
            }

            public uint ReserveQueries(string name, uint count)
            {
                lock (_pendingQueries)
                {
                    var start = _queryIndex;
                    _queryIndex += count;
                    _pendingQueries.Add(new GpuQuery(name, start));
                    return start;
                }
            }
        }
        private readonly struct GpuSectionTracker : IDisposable
        {
            private readonly string _name;
            private readonly VkCommandBuffer _cmd;
            private readonly PerFrameData _frame;
            private readonly uint _startIndex;

            public GpuSectionTracker(string name, VkCommandBuffer cmd, PerFrameData frame)
            {
                _name = name;
                _cmd = cmd;
                _frame = frame;
                _startIndex = _frame.ReserveQueries(name, 2);

                // Write start timestamp
                _cmd.WriteTimestamp(PipelineStageFlags.TopOfPipeBit, _frame.QueryPool, _startIndex);
            }

            public void Dispose()
            {
                // Write end timestamp
                _cmd.WriteTimestamp(PipelineStageFlags.BottomOfPipeBit, _frame.QueryPool, _startIndex + 1);
            }
        }

        private record struct GpuQuery(string Name, uint StartIndex);
    }
}