using RockEngine.Core;
using RockEngine.Core.Builders;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.SubPasses;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Editor.SubPasses
{
    public class ImGuiPass : IRenderSubPass
    {
        private readonly GraphicsEngine _graphicsEngine;
        private readonly IndirectCommandManager _commandManager;

        public ImGuiPass(
            GraphicsEngine graphicsEngine, 
            IndirectCommandManager commandManager)
        {
            _graphicsEngine = graphicsEngine;
            _commandManager = commandManager;
        }

        public uint Order => 0;

       

        public void Execute(VkCommandBuffer cmd, params object[] args)
        {
            var renderer = (Renderer)args[0];
            using (PerformanceTracer.BeginSection(nameof(ImGuiPass)))
            {

                while (_commandManager.TryDequeue(out var command))
                {
                    if (command is ImguiRenderCommand imguiCmd)
                    {
                        imguiCmd.RenderCommand(cmd, _graphicsEngine.FrameIndex);
                    }
                    else
                    {
                        // If it's not an ImGui command, put it back in the queue
                        _commandManager.AddCommand(command);
                        break;
                    }
                }
            }
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
                .Add();
        }

        public void Initilize()
        {
        }
    }
}
