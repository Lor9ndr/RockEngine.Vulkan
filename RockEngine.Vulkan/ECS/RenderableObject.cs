using RockEngine.Vulkan.VkBuilders;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.ECS
{
    internal class RenderableObject
    {
        public Vertex[] Vertices { get; private set; }
        public uint[]? Indicies { get; private set; }

        private BufferWrapper _vertexBuffer;
        private BufferWrapper? _indexBuffer;

        public RenderableObject(Vertex[] vertices, uint[]? indicies = null)
        {
            Vertices = vertices;
            Indicies = indicies;
        }

    }
}