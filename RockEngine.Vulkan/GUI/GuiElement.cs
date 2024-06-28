using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

using System.Numerics;

namespace RockEngine.Vulkan.GUI
{
    public abstract class GuiElement
    {
        public Vector2 Position { get; set; }
        public Vector2 Size { get; set; }
        public Vector4 Color { get; set; }

        public uint VertexOffset { get; set; } // Offset in the shared buffer
        public abstract uint VertexCount { get; }

        public abstract Task Render(VulkanContext context, CommandBufferWrapper commandBuffer, BufferWrapper sharedBuffer);
        public abstract Task UpdateBuffer(VulkanContext context, BufferWrapper sharedBuffer);
    }
}
