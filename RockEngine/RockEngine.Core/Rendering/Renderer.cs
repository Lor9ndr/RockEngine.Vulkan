using RockEngine.Core.ECS.Components;
using RockEngine.Core.Extensions.Builders;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Core.Rendering.PipelineRenderers;
using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Core.Rendering.RockEngine.Core.Rendering;
using RockEngine.Vulkan;
using RockEngine.Vulkan.Builders;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering
{
    public class Renderer : IDisposable
    {
        private readonly VulkanContext _context;

        public EngineRenderPass RenderPass { get; }

        private readonly IRenderPipeline _renderPipeline;
        private readonly LightManager _lightManager;
        private readonly TransformManager _transformManager;
        private readonly CameraManager _cameraManager;
        private readonly IndirectCommandManager _indirectCommandManager;
        private readonly BindingManager _bindingManager;
        private readonly GraphicsEngine _graphicsEngine;
        private readonly PipelineManager _pipelineManager;
        private VkPipeline _deferredLightingPipeline;
        private VkPipeline _screenPipeline;

        public SubmitContext SubmitContext => _context.SubmitContext;
        public SwapchainRenderTarget SwapchainTarget { get; }

        public uint FrameIndex => _graphicsEngine.CurrentImageIndex;

        public const ulong MAX_LIGHTS_SUPPORTED = 10_000;
        public GlobalUbo GlobalUbo { get; } = new GlobalUbo("GlobalData", 0);

        public LightManager LightManager => _lightManager;

        public VkPipeline DeferredLightingPipeline => _deferredLightingPipeline;

        public PipelineManager PipelineManager =>_pipelineManager;

        public BindingManager BindingManager => _bindingManager;

        public Renderer(
                  VulkanContext context,
                  GraphicsEngine graphicsEngine,
                  PipelineManager pipelineManager)
        {
            var poolSizes = new[]
            {
                new DescriptorPoolSize(DescriptorType.UniformBuffer, 5_000),
                new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 5_000),
                new DescriptorPoolSize(DescriptorType.StorageBuffer, 5_000),
                new DescriptorPoolSize(DescriptorType.InputAttachment, 300),
                new DescriptorPoolSize(DescriptorType.UniformBufferDynamic, 2_000)
            };
            _context = context;
            _graphicsEngine = graphicsEngine;
            _pipelineManager = pipelineManager;
            SwapchainTarget = new SwapchainRenderTarget(context, graphicsEngine.Swapchain);

            RenderPass = CreateRenderPass();
            CreateLightingResources();
            CreateScreenPipeline();

            // Инициализация всех менеджеров
            _lightManager = new LightManager(context, (uint)_context.MaxFramesPerFlight, MAX_LIGHTS_SUPPORTED);
            _transformManager = new TransformManager(context, (uint)_context.MaxFramesPerFlight);
            _cameraManager = new CameraManager(context, graphicsEngine, RenderPass);
            _indirectCommandManager = new IndirectCommandManager(context, TransformManager.Capacity);
            _bindingManager = new BindingManager(context, new DescriptorPoolManager(context, poolSizes, 5_000), graphicsEngine);


            _renderPipeline = new DeferredRenderPipeline( _context,
                new GeometryPass(_context,_bindingManager, _transformManager, _indirectCommandManager, GlobalUbo),
                new LightingPass(_context,_bindingManager, _lightManager, _deferredLightingPipeline),
                new ScreenPass(_context,_bindingManager, _screenPipeline, SwapchainTarget),
                new ImGuiPass(_context,_bindingManager, _graphicsEngine.Swapchain, _indirectCommandManager.OtherCommands)
            );

        }

        public async Task Render(VkCommandBuffer primaryCmdBuffer)
        {
            using (PerformanceTracer.BeginSection("Frame Render"))
            {
                // Execute main rendering pipeline
                await _renderPipeline.Execute(primaryCmdBuffer, _cameraManager, this);
            }
        }

        public async Task UpdateFrameData()
        {
            // Update all frame data first
            await _lightManager.Update(_cameraManager.ActiveCameras);
            await _transformManager.Update(FrameIndex);
            await _indirectCommandManager.Update();
            await _renderPipeline.Update();
        }

        private EngineRenderPass CreateRenderPass()
        {
            var mainPassBuilder = new RenderPassBuilder(_context)
                // GBuffer Color Attachments (0-2)
                .ConfigureAttachment(GBuffer.ColorAttachmentFormats[0])
                    .WithColorOperations(
                        load: AttachmentLoadOp.Clear,
                        store: AttachmentStoreOp.Store,
                        initialLayout: ImageLayout.Undefined,
                        finalLayout: ImageLayout.ShaderReadOnlyOptimal)
                    .Add()
                .ConfigureAttachment(GBuffer.ColorAttachmentFormats[1])
                    .WithColorOperations(
                        load: AttachmentLoadOp.Clear,
                        store: AttachmentStoreOp.Store,
                        initialLayout: ImageLayout.Undefined,
                        finalLayout: ImageLayout.ShaderReadOnlyOptimal)
                    .Add()
                .ConfigureAttachment(GBuffer.ColorAttachmentFormats[2])
                    .WithColorOperations(
                        load: AttachmentLoadOp.Clear,
                        store: AttachmentStoreOp.Store,
                        initialLayout: ImageLayout.Undefined,
                        finalLayout: ImageLayout.ShaderReadOnlyOptimal)
                    .Add()
                // Depth Attachment (3)
                .ConfigureAttachment(_graphicsEngine.Swapchain.DepthFormat)
                    .WithDepthOperations(
                        load: AttachmentLoadOp.Clear,
                        store: AttachmentStoreOp.DontCare,
                        initialLayout: ImageLayout.Undefined,
                        finalLayout: ImageLayout.DepthStencilAttachmentOptimal)
                    .Add()
                // Swapchain Color Attachment (4)
                .ConfigureAttachment(_graphicsEngine.Swapchain.Format)
                    .WithColorOperations(
                        load: AttachmentLoadOp.Clear,
                        store: AttachmentStoreOp.Store,
                        initialLayout: ImageLayout.ColorAttachmentOptimal,
                        finalLayout: ImageLayout.ColorAttachmentOptimal)
                    .Add();

            // Subpass 0: Geometry Pass
            mainPassBuilder.BeginSubpass()
                .AddColorAttachment(0, ImageLayout.ColorAttachmentOptimal)
                .AddColorAttachment(1, ImageLayout.ColorAttachmentOptimal)
                .AddColorAttachment(2, ImageLayout.ColorAttachmentOptimal)
                .SetDepthAttachment(3, ImageLayout.DepthStencilAttachmentOptimal)
                .EndSubpass();

            // Subpass 1: Lighting Pass
            mainPassBuilder.BeginSubpass()
                .AddInputAttachment(0, ImageLayout.ShaderReadOnlyOptimal)
                .AddInputAttachment(1, ImageLayout.ShaderReadOnlyOptimal)
                .AddInputAttachment(2, ImageLayout.ShaderReadOnlyOptimal)
                .AddColorAttachment(4, ImageLayout.ColorAttachmentOptimal)
                .EndSubpass();

            // Dependencies
            mainPassBuilder.AddDependency()
                .FromExternal()
                .ToSubpass(0)
                .WithStages(
                    PipelineStageFlags.BottomOfPipeBit,
                    PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit)
                .WithAccess(
                    AccessFlags.None,
                    AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit)
                .Add();

            mainPassBuilder.AddDependency()
                .FromSubpass(0)
                .ToSubpass(1)
                .WithStages(
                    PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.LateFragmentTestsBit,
                    PipelineStageFlags.FragmentShaderBit)
                .WithAccess(
                    AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
                    AccessFlags.ShaderReadBit)
                .Add();

            return mainPassBuilder.Build(_graphicsEngine.RenderPassManager, "DeferredRenderPass");
        }


        private unsafe void CreateLightingResources()
        {
            var vertShader = VkShaderModule.Create(_context, "Shaders/deferred_lighting.vert.spv", ShaderStageFlags.VertexBit);
            var fragShader = VkShaderModule.Create(_context, "Shaders/deferred_lighting.frag.spv", ShaderStageFlags.FragmentBit);

            var pipelineLayout = VkPipelineLayout.Create(_context, vertShader, fragShader);

            var colorBlendAttachments = new PipelineColorBlendAttachmentState[1];
            colorBlendAttachments[0] = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                 ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                 BlendEnable = false,
            };

            using var pipelineBuilder = new GraphicsPipelineBuilder(_context, "DeferredLighting")
                .WithShaderModule(vertShader)
                .WithShaderModule(fragShader)
                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder())
                .WithInputAssembly(new VulkanInputAssemblyBuilder().Configure())
                .WithViewportState(new VulkanViewportStateInfoBuilder()
                    .AddViewport(new Viewport(0, 0, _graphicsEngine.Swapchain.Extent.Width,
                                             _graphicsEngine.Swapchain.Extent.Height, 0, 1))
                    .AddScissors(new Rect2D(new Offset2D(), _graphicsEngine.Swapchain.Extent)))
                .WithRasterizer(new VulkanRasterizerBuilder().CullFace(CullModeFlags.None))
                .WithMultisampleState(new VulkanMultisampleStateInfoBuilder().Configure(false, SampleCountFlags.Count1Bit))
                .WithColorBlendState(new VulkanColorBlendStateBuilder().AddAttachment(colorBlendAttachments))
                .AddRenderPass(RenderPass)
                .WithSubpass(1)
                .WithPipelineLayout(pipelineLayout)
                 .AddDepthStencilState(new PipelineDepthStencilStateCreateInfo()
                 {
                     SType = StructureType.PipelineDepthStencilStateCreateInfo,
                     DepthTestEnable = false,
                     DepthWriteEnable = false,
                     DepthCompareOp = CompareOp.Always,
                     DepthBoundsTestEnable = false,
                     MinDepthBounds = 0.0f,
                     MaxDepthBounds = 1.0f,
                     StencilTestEnable = false,
                 })
                .WithDynamicState(new PipelineDynamicStateBuilder()
                    .AddState(DynamicState.Viewport)
                    .AddState(DynamicState.Scissor));

            _deferredLightingPipeline = _pipelineManager.Create(pipelineBuilder);
        }
        private unsafe void CreateScreenPipeline()
        {
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
                .AddRenderPass(SwapchainTarget.RenderPass)
                .WithSubpass(0)
                .WithPipelineLayout(pipelineLayout);

            _screenPipeline = _pipelineManager.Create(pipelineBuilder);
        }


        public void Draw(Mesh mesh)
        {
            var transformIndex = _transformManager.AddTransform(mesh.Entity.Transform.GetModelMatrix());
            _indirectCommandManager.AddMesh(mesh, transformIndex);
        }

        public void AddCommand(IRenderCommand command)
        {
            _indirectCommandManager.AddCommand(command);
        }

        public void RegisterCamera(Camera camera) => _cameraManager.Register(camera,this);
        public void Dispose() => _renderPipeline.Dispose();
    }
}
