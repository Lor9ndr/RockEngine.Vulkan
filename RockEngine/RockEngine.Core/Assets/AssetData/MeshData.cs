using RockEngine.Core.Attributes;

namespace RockEngine.Core.Assets.AssetData
{
    public class MeshData<TVertex> where TVertex : IVertex
    {
        public TVertex[] Vertices { get;set;}
        public uint[]? Indices { get; set; }

        public MeshData()
        {
        }

        public MeshData(TVertex[] vertices, uint[]? indices)
        {
            Vertices = vertices;
            Indices = indices;
        }
    }
}
