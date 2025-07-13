using RockEngine.Core.Builders;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.PipelineRenderers;
using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using SkiaSharp;

namespace RockEngine.Core.Rendering.Passes
{
    public class ScreenPass : IRenderSubPass
    {
        private readonly VulkanContext _context;
        private readonly BindingManager _bindingManager;
        private readonly PipelineManager _pipelineManager;
        private readonly RenderPassManager _renderPassManager;
        private readonly GraphicsEngine _graphicsEngine;
        private readonly Renderer _renderer;
        private Material _screenMaterial;
        protected Dictionary<Texture, TextureBinding> Bindings = new Dictionary<Texture, TextureBinding>();
        private VkPipeline _screenPipeline;

        public uint Order => 0;

        public ScreenPass(
            VulkanContext context,
            BindingManager bindingManager,
            PipelineManager pipelineManager,
            RenderPassManager renderPassManager,
            GraphicsEngine graphicsEngine,
            Renderer renderer)
        {
            _context = context;
            _bindingManager = bindingManager;
            _pipelineManager = pipelineManager;
            _renderPassManager = renderPassManager;
            _graphicsEngine = graphicsEngine;
            _renderer = renderer;
        }

     
        public Task Execute(VkCommandBuffer cmd, params object[] args)
        {
            var renderer = args[0] as Renderer ?? throw new ArgumentNullException(nameof(Renderer));
            var camera = args[1] as Camera ?? throw new ArgumentNullException(nameof(Camera));
            
            SetInputTexture(camera.RenderTarget.OutputTexture);

            cmd.SetViewport(_renderer.SwapchainTarget.Viewport);
            cmd.SetScissor(_renderer.SwapchainTarget.Scissor);

            cmd.BindPipeline(_screenPipeline, PipelineBindPoint.Graphics);
            if(!_screenMaterial.IsComplete)
            {
                return Task.CompletedTask;
            }
            _bindingManager.BindResourcesForMaterial(renderer.FrameIndex, _screenMaterial, cmd);
            cmd.Draw(3, 1, 0, 0);
            return Task.CompletedTask;
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
                    .AddViewport(new Viewport(0, 0, _graphicsEngine.Swapchain.Extent.Width,
                                             _graphicsEngine.Swapchain.Extent.Height, 0, 1))
                    .AddScissors(new Rect2D(new Offset2D(), _graphicsEngine.Swapchain.Extent)))
                .WithRasterizer(new VulkanRasterizerBuilder().CullFace(CullModeFlags.None))
                .WithMultisampleState(new VulkanMultisampleStateInfoBuilder().Configure(false, SampleCountFlags.Count1Bit))
                .WithColorBlendState(new VulkanColorBlendStateBuilder().AddDefaultAttachment())
                .AddRenderPass(renderPass)
                .WithSubpass(0)
                .WithPipelineLayout(pipelineLayout);

            
            _screenPipeline = _pipelineManager.Create(pipelineBuilder);
            _screenMaterial = new Material(_screenPipeline);
        }

        internal void SetInputTexture(Texture outputTexture)
        {
            if(!Bindings.TryGetValue(outputTexture, out var binding))
            {
                binding = new TextureBinding(0, 0, ImageLayout.ShaderReadOnlyOptimal, outputTexture);
                Bindings.Add(outputTexture, binding);
            }
            _screenMaterial.Bind(binding);
        }
        public void SetupAttachmentDescriptions(RenderPassBuilder builder)
        {
            // Color attachment (swapchain image)
            builder.ConfigureAttachment(_graphicsEngine.Swapchain.Format)
                .WithColorOperations(
                    load: AttachmentLoadOp.Clear,
                    store: AttachmentStoreOp.Store,
                    initialLayout: ImageLayout.Undefined,
                    finalLayout: ImageLayout.PresentSrcKhr)
                .Add();

            // Depth attachment
            builder.ConfigureAttachment(_graphicsEngine.Swapchain.DepthFormat)
                .WithDepthOperations(
                    load: AttachmentLoadOp.Clear,
                    store: AttachmentStoreOp.DontCare,
                    initialLayout: ImageLayout.Undefined,
                    finalLayout: ImageLayout.DepthStencilAttachmentOptimal)
                .Add();
        }

        public void SetupSubpassDescription(RenderPassBuilder.SubpassConfigurer subpass)
        {
            // Color output
            subpass.AddColorAttachment(0);

            // Depth testing
            subpass.SetDepthAttachment(1);
        }

        public void SetupDependencies(RenderPassBuilder builder, uint subpassIndex)
        {
            /*builder.AddDependency()
                .FromExternal()
                .ToSubpass(subpassIndex)
                .WithStages(
                    PipelineStageFlags.TopOfPipeBit,
                    PipelineStageFlags.ColorAttachmentOutputBit |
                    PipelineStageFlags.EarlyFragmentTestsBit)
                .WithAccess(
                    AccessFlags.None,
                    AccessFlags.ColorAttachmentWriteBit |
                    AccessFlags.DepthStencilAttachmentWriteBit)
                .Add();*/
        }
        public void Dispose()
        {
        }

      
    }
}