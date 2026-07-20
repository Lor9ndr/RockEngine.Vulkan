
using MemoryPack;
using RockEngine.Assets;

namespace RockEngine.Core.Assets
{
    [MemoryPackable]
    public partial class ModelData : IPolymorphicSerializable
    {
        public List<ModelPartData> Parts { get; set; } = new List<ModelPartData>();
    }
}