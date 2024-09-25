using Silk.NET.Vulkan;

using System.Buffers;

namespace RockEngine.Vulkan.Builders
{
    public class VulkanViewportStateInfoBuilder : DisposableBuilder
    {
        public List<Rect2D> _scissors = new List<Rect2D>();
        public List<Viewport> _viewports = new List<Viewport>();
        public VulkanViewportStateInfoBuilder AddScissors(Rect2D scissor)
        {
            _scissors.Add(scissor);
            return this;
        }
        public VulkanViewportStateInfoBuilder AddViewport(Viewport viewport)
        {
            _viewports.Add(viewport);
            return this;
        }

        public unsafe MemoryHandle Build()
        {
            return CreateMemoryHandle([new PipelineViewportStateCreateInfo()
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ScissorCount = (uint)_scissors.Count,
                PScissors = (Rect2D*)CreateMemoryHandle(_scissors.ToArray()).Pointer,
                PViewports = (Viewport*)CreateMemoryHandle(_viewports.ToArray()).Pointer,
                ViewportCount = (uint)_viewports.Count
            }]);
        }
    }
}
