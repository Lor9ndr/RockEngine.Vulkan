using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Commands
{
    public record struct ImguiRenderCommand(Action<VkCommandBuffer, uint> RenderCommand) : IRenderCommand
    {
    }
}
