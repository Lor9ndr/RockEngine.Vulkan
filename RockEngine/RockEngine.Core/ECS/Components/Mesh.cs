namespace RockEngine.Core.ECS.Components
{
    public struct Mesh
    {
        public Vertex[] Vertices;
        public int[]? Indices;

        public readonly bool HasIndices => Indices?.Length > 0;

        public Mesh(Vertex[] vertices, int[]? indices = null)
        {
            Vertices = vertices;
            Indices = indices;
        }
    }
}
