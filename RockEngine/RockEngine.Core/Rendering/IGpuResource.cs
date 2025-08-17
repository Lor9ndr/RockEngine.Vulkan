namespace RockEngine.Core.Rendering
{
    public interface IGpuResource
    {
        bool GpuReady { get; }
        ValueTask LoadGpuResourcesAsync();
        void UnloadGpuResources();
    }
}
