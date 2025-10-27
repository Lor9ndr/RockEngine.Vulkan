namespace RockEngine.Core.Assets.AssetData
{
    public class MeshData<TVertex> where TVertex : IVertex
    {
        public TVertex[] Vertices;
        public uint[]? Indices;

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
