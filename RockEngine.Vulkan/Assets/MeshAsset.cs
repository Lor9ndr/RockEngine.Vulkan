using System.Text.Json.Serialization;

namespace RockEngine.Vulkan.Assets
{
    public class MeshAsset : IAsset
    {
        private Vertex[] _vertices;
        private uint[]? _indices;

        public Guid ID { get; private set; }

        public string Name { get; set; }
        public string Path {get;set;}
        public bool IsChanged { get; set; }

        public Vertex[] Vertices { get => _vertices; set => _vertices = value; }
        public uint[]? Indices { get => _indices; set => _indices = value; }

         

        public MeshAsset(string name, string path)
        {
            Name = name;
            Path = path;
            ID = Guid.NewGuid();
        }

        public MeshAsset(MeshData meshData)
        {
            Name = meshData.Name;
            Path = string.Empty;
            ID = Guid.NewGuid();
            _vertices = meshData.Vertices;
            _indices = meshData.Indices;
        }

        public void SetVertices(Vertex[] vertices)
        {
            _vertices = vertices;
        }

        public void SetIndices(uint[] indices)
        {
            _indices = indices;
        }
    }
}
