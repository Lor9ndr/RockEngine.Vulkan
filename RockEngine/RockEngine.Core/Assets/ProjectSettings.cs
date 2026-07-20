using MemoryPack;

namespace RockEngine.Core.Assets
{
    [MemoryPackable]
    public partial class ProjectSettings
    {
        public string EngineVersion { get; set; } = "1.0.0";
        public string DefaultScene { get; set; } = string.Empty;
        public bool EnableHotReload { get; set; } = true;
        public int MaxAssetCacheSizeMB { get; set; } = 1024;

        // Graphics settings
        public bool VSync { get; set; } = true;
        public int MSAA { get; set; } = 4;
        public int MaxTextureSize { get; set; } = 4096;

        // Build settings
        public bool DevelopmentBuild { get; set; } = true;
        public List<string> BuildScenes { get; set; } = new();

        // Asset pipeline
        public bool AutoGenerateMipmaps { get; set; } = true;
        public bool CompressTextures { get; set; } = false;
    }
}