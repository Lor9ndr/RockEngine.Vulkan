using MessagePack;

namespace RockEngine.Core.Assets
{
    [MessagePackObject]
    public class ProjectSettings
    {
        [Key(0)]
        public string EngineVersion { get; set; } = "1.0.0";
        [Key(1)]
        public string DefaultScene { get; set; } = string.Empty;
        [Key(2)]
        public bool EnableHotReload { get; set; } = true;
        [Key(3)]
        public int MaxAssetCacheSizeMB { get; set; } = 1024;

        // Graphics settings
        [Key(4)]
        public bool VSync { get; set; } = true;
        [Key(5)]
        public int MSAA { get; set; } = 4;
        [Key(6)]
        public int MaxTextureSize { get; set; } = 4096;
        
        [Key(7)]
        // Build settings
        public bool DevelopmentBuild { get; set; } = true;
        [Key(8)]
        public List<string> BuildScenes { get; set; } = new();

        // Asset pipeline
        [Key(9)]
        public bool AutoGenerateMipmaps { get; set; } = true;
        [Key(10)]
        public bool CompressTextures { get; set; } = false;
    }
}