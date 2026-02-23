using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Passes
{
    public interface IPipelineStatisticsProvider : IRenderPassStrategy
    {
        bool PipelineStatisticsSupported { get; }
        bool PipelineStatisticsEnabled { get; set; }

        PipelineStatisticsData GetCurrentStatistics(uint frameIndex);
        PipelineStatisticsData[] GetStatisticsHistory();
        void ResetStatistics();

        QueryPipelineStatisticFlags GetPipelineStatisticsFlags();
        void SetPipelineStatisticsFlags(QueryPipelineStatisticFlags flags);
    }
}