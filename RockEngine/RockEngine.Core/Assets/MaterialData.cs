using MemoryPack;

namespace RockEngine.Core.Assets
{
    [MemoryPackable]
    public partial class MaterialData
    {
        public string PipelineName { get; set; } = "Default";

        public Dictionary<string, AssetReference<TextureAsset>> Textures { get; set; } = new();

        [MemoryPackIgnore]
        public Dictionary<string, object> Parameters { get; set; } = new();
    }
}