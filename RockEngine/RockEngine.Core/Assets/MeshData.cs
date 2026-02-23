using MessagePack;

namespace RockEngine.Core.Assets
{
    [MessagePackObject]
    public class MeshData<T> where T : struct, IVertex
    {
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

        [Key(0)]
        public T[] Vertices { get; set; } = Array.Empty<T>();
        [Key(1)]
        public uint[]? Indices { get; set; }
        [Key(2)]
        public string Name { get; set; } = string.Empty;
    }
}