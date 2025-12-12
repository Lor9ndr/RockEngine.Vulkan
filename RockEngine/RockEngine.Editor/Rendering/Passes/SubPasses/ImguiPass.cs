using RockEngine.Core;
using RockEngine.Core.Builders;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Editor.Rendering.Passes.SubPasses
{
    public class ImGuiPass : IRenderSubPass
    {
        private readonly GraphicsContext _graphicsEngine;
        private readonly IndirectCommandManager _commandManager;

        public ImGuiPass(
            GraphicsContext graphicsEngine, 
            IndirectCommandManager commandManager)
        {
            _graphicsEngine = graphicsEngine;
            _commandManager = commandManager;
        }

        public static uint Order => 0;
        public static string Name => "imgui";

        public SubPassMetadata GetMetadata()
        {
            return new(Order, Name);
        }

        public void Execute(UploadBatch batch, params object[] args)
        {
            var renderer = (WorldRenderer)args[0];
            using (PerformanceTracer.BeginSection(nameof(ImGuiPass)))
            {

                while (_commandManager.TryDequeue(out var command))
                {
                    if (command is ImguiRenderCommand imguiCmd)
                    {
                        imguiCmd.RenderCommand(batch,  _graphicsEngine.FrameIndex, renderer);
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
            builder.ConfigureAttachment(_graphicsEngine.MainSwapchain.Format)
                .WithColorOperations(
                    load: AttachmentLoadOp.Clear, // Preserve existing content
                    store: AttachmentStoreOp.Store,
                    initialLayout: ImageLayout.Undefined,
                    finalLayout: ImageLayout.PresentSrcKhr)
                .Add();

            // Depth attachment (optional)
            builder.ConfigureAttachment(_graphicsEngine.MainSwapchain.DepthFormat)
                .WithDepthOperations(
                    load: AttachmentLoadOp.DontCare,
                    store: AttachmentStoreOp.DontCare,
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
            // Frame-to-frame synchronization: Previous frame's render pass -> Current frame's render pass
            builder.AddDependency()
                .FromExternal()
                .ToSubpass(subpassIndex)
                .WithStages(
                    PipelineStageFlags.LateFragmentTestsBit | PipelineStageFlags.ColorAttachmentOutputBit,
                    PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.ColorAttachmentOutputBit)
                .WithAccess(
                    AccessFlags.DepthStencilAttachmentWriteBit | AccessFlags.ColorAttachmentWriteBit,
                    AccessFlags.DepthStencilAttachmentWriteBit | AccessFlags.ColorAttachmentWriteBit)
                .Add();

            // Current frame's render pass -> Next frame/presentation
            builder.AddDependency()
                .FromSubpass(subpassIndex)
                .ToExternal()
                .WithStages(
                    PipelineStageFlags.LateFragmentTestsBit | PipelineStageFlags.ColorAttachmentOutputBit,
                    PipelineStageFlags.BottomOfPipeBit)
                .WithAccess(
                    AccessFlags.DepthStencilAttachmentWriteBit | AccessFlags.ColorAttachmentWriteBit,
                    AccessFlags.None)
                .Add();
        }

        public void Initilize()
        {
        }
    }
}
