using MemoryPack;

namespace RockEngine.Core.Assets
{
    [MemoryPackable]
    public partial class MeshData<T> where T : struct, IVertex
    {
        [MemoryPackConstructor]
        public MeshData()
        {
        }

        public MeshData(T[] vertices, uint[]? indices)
        {
            Vertices = vertices;
            Indices = indices;
        }

        public MeshData(T[] vertices, uint[]? indices, string name)
        {
            Vertices = vertices;
            Indices = indices;
            Name = name;
        }

        public T[] Vertices { get; set; } = Array.Empty<T>();
        public uint[]? Indices { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}