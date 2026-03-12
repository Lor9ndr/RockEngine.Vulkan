using RockEngine.Core.Extensions;
using RockEngine.Core.Helpers;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Collections.Concurrent;

using ZLinq;

namespace RockEngine.Core.Rendering.Passes
{
    // Query scope for managing queries within a frame
    public struct QueryScope : IDisposable
    {
        private readonly PipelineStatisticsPassStrategyBase _strategy;
        private readonly uint _frameIndex;
        private readonly uint _cameraIndex;
        private readonly uint _subpassIndex;
        private readonly UploadBatch _batch;
        private bool _disposed;

        public QueryScope(
            PipelineStatisticsPassStrategyBase strategy,
            UploadBatch batch,
            uint frameIndex,
            uint cameraIndex,
            uint subpassIndex)
        {
            _strategy = strategy;
            _batch = batch;
            _frameIndex = frameIndex;
            _cameraIndex = cameraIndex;
            _subpassIndex = subpassIndex;
            _disposed = false;
           
            _strategy?.BeginSubpassPipelineStatistics(_batch, _frameIndex, _cameraIndex, _subpassIndex);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _strategy?.EndSubpassPipelineStatistics(_batch, _frameIndex, _cameraIndex, _subpassIndex);
                _disposed = true;
            }
        }
    }

    // Frame query data structure
    public class FrameQueryData
    {
        public uint FrameIndex { get; }
        public VkQueryPool QueryPool { get; }
        public ConcurrentDictionary<uint, CameraQueryInfo> CameraQueries { get; } = new();
        public int TotalQueries => CameraQueries.AsValueEnumerable().Sum(c => c.Value.SubpassCount);
        private readonly Lock _lock = new();

        public FrameQueryData(uint frameIndex, VkQueryPool queryPool)
        {
            FrameIndex = frameIndex;
            QueryPool = queryPool;
        }

        public CameraQueryInfo GetOrAddCameraQueryInfo(uint cameraIndex, int subpassCount)
        {
            return CameraQueries.GetOrAdd(cameraIndex, key =>
            {
                lock (_lock)
                {
                    // Вычисляем стартовый индекс для новой камеры
                    uint startIndex = 0;
                    foreach (var existing in CameraQueries.Values.OrderBy(c => c.StartQueryIndex))
                    {
                        if (existing.StartQueryIndex == startIndex)
                        {
                            startIndex += (uint)existing.SubpassCount;
                        }
                        else
                        {
                            break;
                        }
                    }
                    return new CameraQueryInfo(cameraIndex, subpassCount, startIndex);
                }
            });
        }
    }

    // Camera query information
    public class CameraQueryInfo
    {
        public uint CameraIndex { get; }
        public int SubpassCount { get; }
        public uint StartQueryIndex { get; }

        public CameraQueryInfo(uint cameraIndex, int subpassCount, uint startQueryIndex)
        {
            CameraIndex = cameraIndex;
            SubpassCount = subpassCount;
            StartQueryIndex = startQueryIndex;
        }

        public uint GetQueryIndex(uint subpassIndex) => StartQueryIndex + subpassIndex;
    }

    public abstract class PipelineStatisticsPassStrategyBase : PassStrategyBase, IPipelineStatisticsProvider
    {
        protected const int StatisticsHistorySize = 120;
        protected const int InitialQueriesPerFrame = 128;
        protected const int QueryPoolGrowthFactor = 2;

        private readonly RingBuffer<PipelineStatisticsData> _statisticsHistory;
        private readonly Dictionary<uint, FrameQueryData> _frameQueryData;
        private readonly Dictionary<uint, List<PipelineStatisticsData>> _cameraStatistics;
        private readonly Dictionary<uint, PipelineStatisticsData> _frameStatistics;
        private readonly Dictionary<uint, bool> _frameResetFlags = new();
        private readonly Dictionary<uint, int> _pendingResizes = new();

        protected bool _pipelineStatsEnabled = false;
        protected bool _pipelineStatsInitialized = false;
        protected QueryPipelineStatisticFlags _pipelineStatisticsFlags;
        private readonly Lock _semaphoreSlim = new Lock();

        public bool PipelineStatisticsSupported => _pipelineStatsEnabled;
        public bool PipelineStatisticsEnabled { get; set; } = true;

        protected PipelineStatisticsPassStrategyBase(
            VulkanContext context,
            IEnumerable<IRenderSubPass> subpasses)
            : base(context, subpasses)
        {
            _statisticsHistory = new RingBuffer<PipelineStatisticsData>(StatisticsHistorySize);
            _frameQueryData = new Dictionary<uint, FrameQueryData>();
            _cameraStatistics = new Dictionary<uint, List<PipelineStatisticsData>>();
            _frameStatistics = new Dictionary<uint, PipelineStatisticsData>();

            _pipelineStatisticsFlags = QueryPipelineStatisticFlags.InputAssemblyVerticesBit |
                                     QueryPipelineStatisticFlags.InputAssemblyPrimitivesBit |
                                     QueryPipelineStatisticFlags.VertexShaderInvocationsBit |
                                     QueryPipelineStatisticFlags.ClippingInvocationsBit |
                                     QueryPipelineStatisticFlags.ClippingPrimitivesBit |
                                     QueryPipelineStatisticFlags.FragmentShaderInvocationsBit |
                                     QueryPipelineStatisticFlags.ComputeShaderInvocationsBit |
                                     QueryPipelineStatisticFlags.GeometryShaderInvocationsBit |
                                     QueryPipelineStatisticFlags.GeometryShaderPrimitivesBit |
                                     QueryPipelineStatisticFlags.TessellationControlShaderPatchesBit |
                                     QueryPipelineStatisticFlags.TessellationEvaluationShaderInvocationsBit;
        }

        public override void InitializeSubPasses()
        {
            base.InitializeSubPasses();
            InitializePipelineStatistics();
        }

        protected virtual void InitializePipelineStatistics()
        {
            var physicalDeviceFeatures = _context.Device.PhysicalDevice.Features;
            _pipelineStatsEnabled = physicalDeviceFeatures.PipelineStatisticsQuery;

            if (!_pipelineStatsEnabled)
            {
                _logger.Warn("Pipeline statistics queries are not supported on this device");
                return;
            }

            _pipelineStatsInitialized = true;
        }

        protected FrameQueryData GetOrCreateFrameQueryData(uint frameIndex, int requiredQueries = 0)
        {
            return _frameQueryData.GetOrAdd(frameIndex, (frameIdx) =>
            {
                int queryCount = Math.Max(InitialQueriesPerFrame, requiredQueries);

                var queryPoolCreateInfo = new QueryPoolCreateInfo
                {
                    SType = StructureType.QueryPoolCreateInfo,
                    QueryType = QueryType.PipelineStatistics,
                    QueryCount = (uint)queryCount,
                    PipelineStatistics = _pipelineStatisticsFlags
                };

                var queryPool = VkQueryPool.Create(_context, queryPoolCreateInfo);
                queryPool.LabelObject(GetType().Name + $"[{frameIdx}]");
                return new FrameQueryData(frameIdx, queryPool);
            });
        }

        public void BeginFrameQueries(UploadBatch batch, uint frameIndex)
        {
            if (!_pipelineStatsEnabled || !_pipelineStatsInitialized || !PipelineStatisticsEnabled)
                return;
            lock (_semaphoreSlim)
            {
                // Используем атомарную операцию для установки флага сброса
                if (_frameResetFlags.TryAdd(frameIndex, true))
                {
                    // Первый поток, который устанавливает флаг, выполняет сброс
                    var frameData = GetOrCreateFrameQueryData(frameIndex);

                    // Check for pending resize
                    if (_pendingResizes.Remove(frameIndex, out int requiredSize))
                    {
                        ResizeQueryPool(batch, frameIndex, requiredSize);
                        frameData = GetOrCreateFrameQueryData(frameIndex);
                    }

                    // Reset query pool before starting new queries
                    batch.ResetQueryPool(frameData.QueryPool, 0, frameData.QueryPool.QueryCount);
                }
            }
        }

        public QueryScope BeginQueryScope(UploadBatch batch, uint frameIndex, uint cameraIndex, uint subpassIndex)
        {
            if (!_pipelineStatsEnabled || !_pipelineStatsInitialized || !PipelineStatisticsEnabled)
                return new QueryScope(null, null, 0, 0, 0);

            var frameData = GetOrCreateFrameQueryData(frameIndex);

            _ = frameData.GetOrAddCameraQueryInfo(cameraIndex, _subPasses.Length);

            // Check if we need to resize
            if (frameData.TotalQueries > frameData.QueryPool.QueryCount)
            {
                _pendingResizes[frameIndex] = frameData.TotalQueries;
            }

            return new QueryScope(this, batch, frameIndex, cameraIndex, subpassIndex);
        }

        public void BeginSubpassPipelineStatistics(UploadBatch batch, uint frameIndex, uint cameraIndex, uint subpassIndex)
        {
            if (!_pipelineStatsEnabled || !_pipelineStatsInitialized || !PipelineStatisticsEnabled)
                return;

            if (!_frameQueryData.TryGetValue(frameIndex, out var frameData))
                return;


            if (!frameData.CameraQueries.TryGetValue(cameraIndex, out var cameraInfo))
                return;


            uint queryIndex = cameraInfo.GetQueryIndex(subpassIndex);

            if (queryIndex >= frameData.QueryPool.QueryCount)
            {
                _logger.Warn($"Query index {queryIndex} exceeds pool size {frameData.QueryPool.QueryCount}");
                return;
            }

            batch.BeginQuery(frameData.QueryPool, queryIndex, QueryControlFlags.None);
        }

        public void EndSubpassPipelineStatistics(UploadBatch batch, uint frameIndex, uint cameraIndex, uint subpassIndex)
        {
            if (!_pipelineStatsEnabled || !_pipelineStatsInitialized || !PipelineStatisticsEnabled)
                return;

            if (!_frameQueryData.TryGetValue(frameIndex, out var frameData))
                return;

            if (!frameData.CameraQueries.TryGetValue(cameraIndex, out var cameraInfo))
                return;

            uint queryIndex = cameraInfo.GetQueryIndex(subpassIndex);

            if (queryIndex >= frameData.QueryPool.QueryCount)
                return;

            batch.EndQuery(frameData.QueryPool, queryIndex);
        }

        protected void ResizeQueryPool(UploadBatch batch, uint frameIndex, int requiredQueries)
        {
            if (!_frameQueryData.TryGetValue(frameIndex, out var oldFrameData))
                return;

            int newSize = (int)Math.Max(requiredQueries, oldFrameData.QueryPool.QueryCount * QueryPoolGrowthFactor);
            newSize = (int)Math.Pow(2, Math.Ceiling(Math.Log(newSize, 2)));

            _logger.Info($"Resizing query pool for frame {frameIndex} from {oldFrameData.QueryPool.QueryCount} to {newSize} queries");

            var queryPoolCreateInfo = new QueryPoolCreateInfo
            {
                SType = StructureType.QueryPoolCreateInfo,
                QueryType = QueryType.PipelineStatistics,
                QueryCount = (uint)newSize,
                PipelineStatistics = _pipelineStatisticsFlags
            };

            var newQueryPool = VkQueryPool.Create(_context, queryPoolCreateInfo);
            newQueryPool.LabelObject(GetType().Name + $"[{frameIndex}]");

            // Create new frame data with existing camera queries
            var newFrameData = new FrameQueryData(frameIndex, newQueryPool);

            // Copy all camera queries to new frame data
            foreach (var kvp in oldFrameData.CameraQueries)
            {
                newFrameData.CameraQueries[kvp.Key] = kvp.Value;
            }

            // Add old query pool as dependency
            batch.AddDependency(oldFrameData.QueryPool);

            // Update dictionary atomically
            _frameQueryData[frameIndex] = newFrameData;
        }

        protected void RetrievePipelineStatistics(uint frameIndex)
        {
            if (!_pipelineStatsEnabled || !_pipelineStatsInitialized || !PipelineStatisticsEnabled)
                return;

            if (!_frameQueryData.TryGetValue(frameIndex, out var frameData))
                return;

            // Skip if no queries were recorded
            if (frameData.TotalQueries == 0)
                return;

            const int valuesPerQuery = 12;

            // Allocate results buffer
            Span<ulong> results = stackalloc ulong[frameData.TotalQueries * valuesPerQuery];

            var result = frameData.QueryPool.GetResults(
                0,
                (uint)frameData.TotalQueries,
                results,
                valuesPerQuery * sizeof(ulong),
                QueryResultFlags.ResultWithAvailabilityBit | QueryResultFlags.Result64Bit
            );

            if (result == Result.Success || result == Result.NotReady)
            {
                var frameStats = new PipelineStatisticsData
                {
                    FrameIndex = frameIndex,
                    CollectionTime = DateTime.UtcNow,
                };

                foreach (var cameraKvp in frameData.CameraQueries)
                {
                    var cameraInfo = cameraKvp.Value;
                    var cameraStats = new PipelineStatisticsData
                    {
                        FrameIndex = frameIndex,
                        CollectionTime = DateTime.UtcNow,
                    };

                    bool anyDataAvailable = false;

                    for (uint subpass = 0; subpass < cameraInfo.SubpassCount; subpass++)
                    {
                        uint queryIndex = cameraInfo.GetQueryIndex(subpass);
                        int baseResultIndex = (int)queryIndex * valuesPerQuery;

                        ulong availability = results[baseResultIndex];
                        if (availability == 0)
                            continue;

                        anyDataAvailable = true;
                        int statsIndex = baseResultIndex + 1;

                        cameraStats.InputAssemblyVertices += results[statsIndex];
                        cameraStats.InputAssemblyPrimitives += results[statsIndex + 1];
                        cameraStats.VertexShaderInvocations += results[statsIndex + 2];
                        cameraStats.ClippingInvocations += results[statsIndex + 3];
                        cameraStats.ClippingPrimitives += results[statsIndex + 4];
                        cameraStats.FragmentShaderInvocations += results[statsIndex + 5];
                        cameraStats.ComputeShaderInvocations += results[statsIndex + 6];
                        cameraStats.GeometryShaderInvocations += results[statsIndex + 7];
                        cameraStats.GeometryShaderPrimitives += results[statsIndex + 8];
                        cameraStats.TessellationControlShaderPatches += results[statsIndex + 9];
                        cameraStats.TessellationEvaluationShaderInvocations += results[statsIndex + 10];

                        frameStats.InputAssemblyVertices += results[statsIndex];
                        frameStats.InputAssemblyPrimitives += results[statsIndex + 1];
                        frameStats.VertexShaderInvocations += results[statsIndex + 2];
                        frameStats.ClippingInvocations += results[statsIndex + 3];
                        frameStats.ClippingPrimitives += results[statsIndex + 4];
                        frameStats.FragmentShaderInvocations += results[statsIndex + 5];
                        frameStats.ComputeShaderInvocations += results[statsIndex + 6];
                        frameStats.GeometryShaderInvocations += results[statsIndex + 7];
                        frameStats.GeometryShaderPrimitives += results[statsIndex + 8];
                        frameStats.TessellationControlShaderPatches += results[statsIndex + 9];
                        frameStats.TessellationEvaluationShaderInvocations += results[statsIndex + 10];
                    }

                    if (anyDataAvailable)
                    {
                        var cameraBag = _cameraStatistics.GetOrAdd(
                            cameraInfo.CameraIndex,
                            _ => new List<PipelineStatisticsData>());
                        cameraBag.Add(cameraStats);
                    }
                }

                _frameStatistics[frameIndex] = frameStats;
                _statisticsHistory.Push(frameStats);
            }

            // Clear reset flag for next frame
            _frameResetFlags.Remove(frameIndex);
        }

        public PipelineStatisticsData GetCameraStatistics(uint frameIndex, uint cameraIndex)
        {
            if (_cameraStatistics.TryGetValue(cameraIndex, out var bag))
            {
                return bag.FirstOrDefault(s => s.FrameIndex == frameIndex);
            }
            return default;
        }

        public List<PipelineStatisticsData> GetAllCameraStatistics(uint frameIndex)
        {
            var result = new List<PipelineStatisticsData>();
            foreach (var kvp in _cameraStatistics)
            {
                var stats = kvp.Value.FirstOrDefault(s => s.FrameIndex == frameIndex);
                if (stats.FrameIndex == frameIndex)
                    result.Add(stats);
            }
            return result;
        }

        public void ClearOldStatistics(uint currentFrameIndex, uint framesToKeep = 10)
        {
            var framesToRemove = _frameQueryData.Keys
                .Where(frame => frame < currentFrameIndex - framesToKeep)
                .ToList();

            foreach (var frame in framesToRemove)
            {
                if (_frameQueryData.Remove(frame, out var frameData))
                {
                    frameData.QueryPool.Dispose();
                }
                _frameStatistics.Remove(frame);
                _frameResetFlags.Remove(frame);
                _pendingResizes.Remove(frame);
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            foreach (var frameData in _frameQueryData.Values)
            {
                frameData.QueryPool.Dispose();
            }
            _frameQueryData.Clear();
            _cameraStatistics.Clear();
        }

        public PipelineStatisticsData GetCurrentStatistics(uint frameIndex)
        {
            if (!_pipelineStatsEnabled || !_pipelineStatsInitialized)
                return default;

            return _frameStatistics.GetValueOrDefault(frameIndex);
        }

        public PipelineStatisticsData[] GetStatisticsHistory() => _statisticsHistory.ToArray();

        public void ResetStatistics()
        {
            _statisticsHistory.Clear();
            _frameStatistics.Clear();
        }

        public QueryPipelineStatisticFlags GetPipelineStatisticsFlags() => _pipelineStatisticsFlags;

        public void SetPipelineStatisticsFlags(QueryPipelineStatisticFlags flags)
        {
            if (_pipelineStatsInitialized)
            {
                _logger.Warn("Cannot change pipeline statistics flags after initialization");
                return;
            }

            _pipelineStatisticsFlags = flags;
        }
    }
}