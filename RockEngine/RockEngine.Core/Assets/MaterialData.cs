using MessagePack;

namespace RockEngine.Core.Assets
{
    [MessagePackObject]
    public class MaterialData
    {
        [Key(0)]
        public string PipelineName { get; set; } = "Default";

        [Key(1)]
        public Dictionary<string, AssetReference<TextureAsset>> Textures { get; set; } = new();

        [IgnoreMember]
        ///TODO: FIX THE MESSAGE PACK SERIALIZATION. IT DOESNT KNOW HOW TO SERIALIZE VECTOR3 FOR EXAMPLE IF IT IS BOXED
        public Dictionary<string, object> Parameters { get; set; } = new();
    }
}