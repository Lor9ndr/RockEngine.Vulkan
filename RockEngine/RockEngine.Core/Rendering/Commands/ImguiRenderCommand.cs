using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Commands
{
    public record struct ImguiRenderCommand(Action<UploadBatch, uint, WorldRenderer> RenderCommand) : IRenderCommand
    {
    }
}
