using MessagePack;

using System.Numerics;

namespace RockEngine.Core.Assets
{
    [MessagePackObject]
    public struct ModelPartData
    {
        [Key(0)]
        public Guid MeshAssetID { get; set; }
        [Key(1)]
        public Guid MaterialAssetID { get; set; }
        [Key(2)]
        public Matrix4x4 Transform { get; set; }
        [Key(3)]
        public string Name { get; set; }
    }
}