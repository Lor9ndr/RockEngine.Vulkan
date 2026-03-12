namespace RockEngine.Core.Rendering
{
    public interface IMesh
    {
        public Guid ID { get; }
        bool HasIndices => IndicesCount > 0;

        uint IndicesCount { get; }
        uint VerticesCount { get; }
    }
}
