using RockEngine.Core.Builders;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Editor.SubPasses
{
    public class ImGuiPass : IRenderSubPass
    {
        private readonly GraphicsEngine _graphicsEngine;
        private readonly IndirectCommandManager _commandManager;

        public ImGuiPass(
            VulkanContext context,
            GraphicsEngine graphicsEngine, IndirectCommandManager commandManager)
        {
            _graphicsEngine = graphicsEngine;
            _commandManager = commandManager;
        }

        public uint Order => 0;

       

        public Task Execute(VkCommandBuffer cmd, params object[] args)
        {
            while (_commandManager.OtherCommands.TryPeek(out var command) && command is ImguiRenderCommand imguiCmd)
            {
                imguiCmd.RenderCommand(cmd, _graphicsEngine.Swapchain.Extent);
                while (!_commandManager.OtherCommands.TryDequeue(out _)) { }
            }
            return Task.CompletedTask;
        }
        public void Dispose()
        {
        }

        public void SetupAttachmentDescriptions(RenderPassBuilder builder)
        {
            // Color attachment (swapchain image)
            builder.ConfigureAttachment(_graphicsEngine.Swapchain.Format)
                .WithColorOperations(
                    load: AttachmentLoadOp.Clear, // Preserve existing content
                    store: AttachmentStoreOp.Store,
                    initialLayout: ImageLayout.Undefined,
                    finalLayout: ImageLayout.PresentSrcKhr)
                .Add();

            // Depth attachment (optional)
            builder.ConfigureAttachment(_graphicsEngine.Swapchain.DepthFormat)
                .WithDepthOperations(
                    load: AttachmentLoadOp.Clear,
                    store: AttachmentStoreOp.Store,
                    initialLayout: ImageLayout.Undefined,
                    finalLayout: ImageLayout.DepthStencilAttachmentOptimal)
                .Add();
        }

        public void SetupSubpassDescription(RenderPassBuilder.SubpassConfigurer subpass)
        {
            // Only need color attachment
            subpass
              .AddColorAttachment(0, ImageLayout.ColorAttachmentOptimal)
              .SetDepthAttachment(1);
        }

        public void SetupDependencies(RenderPassBuilder builder, uint subpassIndex)
        {
          /*  // Dependency from previous subpass (ScreenPass)
            builder.AddDependency()
                .FromSubpass(subpassIndex - 1)
                .ToSubpass(subpassIndex)
                .WithStages(
                    PipelineStageFlags.ColorAttachmentOutputBit,
                    PipelineStageFlags.ColorAttachmentOutputBit)
                .WithAccess(
                    AccessFlags.ColorAttachmentWriteBit,
                    AccessFlags.ColorAttachmentWriteBit)
                .Add();

            // Dependency to external (presentation)
            builder.AddDependency()
                .FromSubpass(subpassIndex)
                .ToExtenral()
                .WithStages(
                    PipelineStageFlags.ColorAttachmentOutputBit,
                    PipelineStageFlags.BottomOfPipeBit)
                .WithAccess(
                    AccessFlags.ColorAttachmentWriteBit,
                    AccessFlags.None)
                .Add();*/
        }

        public void Initilize()
        {
        }
    }
}
