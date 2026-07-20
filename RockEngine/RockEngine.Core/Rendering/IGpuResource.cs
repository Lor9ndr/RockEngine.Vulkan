using MemoryPack;

namespace RockEngine.Core.Rendering
{
    public interface IGpuResource
    {
        [MemoryPackIgnore]
        bool GpuReady { get; }
        ValueTask LoadGpuResourcesAsync();
        void UnloadGpuResources();
    }
}
