using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Extensions
{
    public static class BatchExtensions
    {
        extension(UploadBatch batch)
        {
            public PerformanceTracer.GpuSectionTracker BeginSection(string name, uint frameIndex)
            {
                return PerformanceTracer.BeginSection(name, batch, frameIndex);
            }
        }
    }
}
