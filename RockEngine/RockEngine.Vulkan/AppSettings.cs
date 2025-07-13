using Silk.NET.Maths;

namespace RockEngine.Vulkan
{
    public class AppSettings
    {
        public AppSettings()
        {
        }

        public required string Name { get;set; }
        public Vector2D<int> LoadSize { get; set; }
        public int MaxFramesPerFlight { get; set; } = 3;
        public bool EnableValidationLayers { get;set; }
        public uint MaxCamerasSupported { get; set; }
    }
}
