using MessagePack;

using RockEngine.Assets;

using System.Numerics;

namespace RockEngine.Core.Assets
{
    [MessagePackObject]
    public class MaterialData:IPolymorphicSerializable
    {
        [Key(0)]
        public string PipelineName { get; set; } = "Default";
        [Key(1)]
        public List<AssetReference<TextureAsset>> Textures { get; set; } = new();
        [Key(2)]
        public Dictionary<string, object> Parameters { get; set; } = new();
    }
}