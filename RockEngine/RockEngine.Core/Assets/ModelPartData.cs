using System.Numerics;
using MemoryPack;

namespace RockEngine.Core.Assets
{
    [MemoryPackable]
    public partial struct ModelPartData
    {
        public Guid MeshAssetID { get; set; }
        public Guid MaterialAssetID { get; set; }
        public Matrix4x4 Transform { get; set; }
        public string Name { get; set; }
    }
}