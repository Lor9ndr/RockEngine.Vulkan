namespace RockEngine.Core.Rendering
{
    public interface IMesh
    {
        public Guid ID { get; }
        bool HasIndices { get; }
        uint? IndicesCount { get; }
        uint VerticesCount { get; }
    }
}
