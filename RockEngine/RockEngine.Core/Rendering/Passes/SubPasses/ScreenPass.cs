using RockEngine.Core.Builders;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Passes.SubPasses
{
    public class ScreenPass : IRenderSubPass
    {
        private readonly VulkanContext _context;
        private readonly BindingManager _bindingManager;
        private readonly PipelineManager _pipelineManager;
        private readonly RenderPassManager _renderPassManager;
        private readonly GraphicsContext _graphicsEngine;
        private readonly WorldRenderer _renderer;
        private MaterialPass _screenMaterialPass;
        protected Dictionary<Texture, TextureBinding> Bindings = new Dictionary<Texture, TextureBinding>();
        private RckPipeline _screenPipeline;

        public static uint Order => 0;

        public static string Name => "screen";



        public ScreenPass(
            VulkanContext context,
            BindingManager bindingManager,
            PipelineManager pipelineManager,
            RenderPassManager renderPassManager,
            GraphicsContext graphicsEngine,
            WorldRenderer renderer)
        {
            _context = context;
            _bindingManager = bindingManager;
            _pipelineManager = pipelineManager;
            _renderPassManager = renderPassManager;
            _graphicsEngine = graphicsEngine;
            _renderer = renderer;
        }

        public SubPassMetadata GetMetadata()
        {
            return new(Order, Name);
        }
        public void Execute(UploadBatch batch, params object[] args)
        {
            using (PerformanceTracer.BeginSection(nameof(ScreenPass)))
            {
                var renderer = args[0] as WorldRenderer ?? throw new ArgumentNullException(nameof(WorldRenderer));
                if (args[1] is not Camera camera)
                {
                    return;
                }

                SetInputTexture(camera.RenderTarget.OutputTexture);

                batch.SetViewport(_renderer.SwapchainTarget.Viewport);
                batch.SetScissor(_renderer.SwapchainTarget.Scissor);

                batch.BindPipeline(_screenPipeline, PipelineBindPoint.Graphics);
               /* if(!_screenMaterial.AddSharedBinding)
                {
                    return;
                }*/
                _bindingManager.BindResourcesForMaterial(renderer.FrameIndex, _screenMaterialPass, batch);
                batch.Draw(3, 1, 0, 0);
            }
        }
        public void Initilize()
        {
            var renderPass = _renderPassManager.GetRenderPass<SwapchainPassStrategy>() ?? throw new Exception($"Unable to get renderPass of {nameof(SwapchainPassStrategy)}");

            var vertShader = VkShaderModule.Create(_context, "Shaders/screen.vert.spv", ShaderStageFlags.VertexBit);
            var fragShader = VkShaderModule.Create(_context, "Shaders/screen.frag.spv", ShaderStageFlags.FragmentBit);

            var pipelineLayout = VkPipelineLayout.Create(_context, vertShader, fragShader);

            using var pipelineBuilder = new GraphicsPipelineBuilder(_context, "Screen")
                .WithShaderModule(vertShader)
                .WithShaderModule(fragShader)
                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder())
                .WithInputAssembly(new VulkanInputAssemblyBuilder().Configure())
                .WithDynamicState(new PipelineDynamicStateBuilder()
                    .AddState(DynamicState.Viewport)
                    .AddState(DynamicState.Scissor))
                .WithViewportState(new VulkanViewportStateInfoBuilder()
                    .AddViewport(new Viewport(0, 0, _graphicsEngine.MainSwapchain.Extent.Width,
                                             _graphicsEngine.MainSwapchain.Extent.Height, 0, 1))
                    .AddScissors(new Rect2D(new Offset2D(), _graphicsEngine.MainSwapchain.Extent)))
                .WithRasterizer(new VulkanRasterizerBuilder().CullFace(CullModeFlags.None))
                .WithMultisampleState(new VulkanMultisampleStateInfoBuilder().Configure(false, SampleCountFlags.Count1Bit))
                .WithColorBlendState(new VulkanColorBlendStateBuilder().AddDefaultAttachment())
                .AddRenderPass(renderPass)
                .WithSubpass(GetMetadata())
                .WithPipelineLayout(pipelineLayout);

            
            _screenPipeline = _pipelineManager.Create(pipelineBuilder);
            _screenMaterialPass = new MaterialPass(_screenPipeline);
        }

        internal void SetInputTexture(Texture outputTexture)
        {
            if(!Bindings.TryGetValue(outputTexture, out var binding))
            {
                binding = new TextureBinding(0, 0, 0,1,outputTexture);
                Bindings.Add(outputTexture, binding);
            }
            _screenMaterialPass.BindResource(binding);
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
                .ToExternal()
                .WithStages(
                    PipelineStageFlags.ColorAttachmentOutputBit,
                    PipelineStageFlags.BottomOfPipeBit)
                .WithAccess(
                    AccessFlags.ColorAttachmentWriteBit,
                    AccessFlags.None)
                .Add();
        }
        public void Dispose()
        {
        }

      
    }
}