using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering
{
    public interface IMeshProvider
    {
        public Guid ID { get; }
        bool HasIndices { get; }
        uint? IndicesCount { get; }
        uint VerticesCount { get; }
        VkBuffer? VertexBuffer { get; }
        VkBuffer? IndexBuffer { get; }
    }
}
