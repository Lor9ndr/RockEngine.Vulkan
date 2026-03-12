using MessagePack;

using RockEngine.Assets;

namespace RockEngine.Core.Assets
{
    [MessagePackObject]
    public class ModelData:IPolymorphicSerializable
    {
        [Key(0)]
        public List<ModelPartData> Parts { get; set; } = new List<ModelPartData>();
    }
}