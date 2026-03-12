using RockEngine.Assets;

using System.Numerics;

namespace RockEngine.Core.Assets
{
    public struct ModelPart
    {
        public AssetReference<MeshAsset> Mesh { get; set; }
        public AssetReference<MaterialAsset> Material { get; set; }
        public Matrix4x4 Transform { get; set; }
        public string Name { get; set; }
    }
}
