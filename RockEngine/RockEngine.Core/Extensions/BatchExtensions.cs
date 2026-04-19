using RockEngine.Core.Diagnostics;
using RockEngine.Vulkan;

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
