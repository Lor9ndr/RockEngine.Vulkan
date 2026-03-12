using RockEngine.Core.Rendering;

namespace RockEngine.Core.ResourceProviders
{
    public class Mesh : IMesh
    {
        public Guid ID {get; private set;}

        public uint IndicesCount { get; private set; }

        public uint VerticesCount { get; private set; }

        public Mesh(Guid id,  uint indicesCount, uint verticesCount)
        {
            ID = id;
            IndicesCount = indicesCount;
            VerticesCount = verticesCount;
        }
    }
}
