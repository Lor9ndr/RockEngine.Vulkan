using System.Numerics;
using RockEngine.Core.Builders;
using RockEngine.Core.CoreObjects;
using RockEngine.Core.DI;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.ECS.Components.UI;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Passes.SubPasses
{
    public class UIPass : IRenderSubPass
    {
        public static uint Order => 2;
        public static string Name => "ui";

        private readonly VulkanContext _context;
        private readonly BindingManager _bindingManager;
        private readonly PipelineManager _pipelineManager;
        private readonly IShaderManager _shaderManager;
        private readonly GraphicsContext _graphicsContext;
        private readonly UIManager _uiManager;
        private readonly GlobalTextureArray _globalTextureArray;
        private Material? _imageMaterial;
        private MaterialPass? _imagePass;
        private Material? _textMaterial;
        private MaterialPass? _textPass;

        public UIPass(VulkanContext context,
                      BindingManager bindingManager,
                      PipelineManager pipelineManager,
                      IShaderManager shaderManager,
                      GraphicsContext graphicsContext,
                      UIManager uiManager,
                      GlobalTextureArray globalTextureArray)
        {
            _context = context;
            _bindingManager = bindingManager;
            _pipelineManager = pipelineManager;
            _shaderManager = shaderManager;
            _graphicsContext = graphicsContext;
            _uiManager = uiManager;
            _globalTextureArray = globalTextureArray;
        }

        public SubPassMetadata GetMetadata() => new(Order, Name);

        public void Initilize()
        {
            var swapchainExtent = _graphicsContext.MainSwapchain.Extent;
            var swapchainFormat = _graphicsContext.MainSwapchain.Format;

            // Global bindless texture layout

            // ---------- Image pipeline ----------
            var vertImage = new Shader(_context, _shaderManager.GetShader("UIQuad.vert"));
            var fragImage = new Shader(_context, _shaderManager.GetShader("UIQuad.frag"));
            var pipelineLayout = new CoreObjects.PipelineLayout(_context, vertImage, fragImage);
            var imagePipelineBuilder = new GraphicsPipelineBuilder(_context, "UIPipeline_Image")
                .WithShaderModule(vertImage)
                .WithShaderModule(fragImage)
                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder()
                    .Add(UIVertex.GetBindingDescription(), UIVertex.GetAttributeDescriptions()))
                .WithInputAssembly(new VulkanInputAssemblyBuilder().Configure(false, PrimitiveTopology.TriangleList))
                .WithViewportState(new VulkanViewportStateInfoBuilder()
                    .AddViewport(new Viewport(0, 0, swapchainExtent.Width, swapchainExtent.Height, 0, 1))
                    .AddScissors(new Rect2D(new Offset2D(), swapchainExtent)))
                .WithRasterizer(new VulkanRasterizerBuilder().CullFace(CullModeFlags.None))
                .WithMultisampleState(new VulkanMultisampleStateInfoBuilder().Configure(false, SampleCountFlags.Count1Bit))
                .WithColorBlendState(new VulkanColorBlendStateBuilder()
                    .AddAttachment(new PipelineColorBlendAttachmentState
                    {
                        BlendEnable = true,
                        SrcColorBlendFactor = BlendFactor.SrcAlpha,
                        DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                        ColorBlendOp = BlendOp.Add,
                        SrcAlphaBlendFactor = BlendFactor.One,
                        DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                        AlphaBlendOp = BlendOp.Add,
                        ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                         ColorComponentFlags.BBit | ColorComponentFlags.ABit
                    }))
                .AddDepthStencilState(new PipelineDepthStencilStateCreateInfo
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = false,
                    DepthWriteEnable = false,
                    DepthCompareOp = CompareOp.Always,
                    StencilTestEnable = false
                })
                .WithPipelineLayout(pipelineLayout)
                .AddRenderPass(IoC.Container.GetInstance<RenderPassManager>().GetRenderPass<DeferredPassStrategy>());

            var imagePipeline = _pipelineManager.Create(imagePipelineBuilder)!;

            // ---------- Text pipeline ----------
            var fragText = new Shader(_context, _shaderManager.GetShader("UIText.frag"));
            pipelineLayout = new CoreObjects.PipelineLayout(_context, vertImage, fragText);

            var textPipelineBuilder = new GraphicsPipelineBuilder(_context, "UIPipeline_Text")
                .WithShaderModule(vertImage)
                .WithShaderModule(fragText)
                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder()
                    .Add(UIVertex.GetBindingDescription(), UIVertex.GetAttributeDescriptions()))
                .WithInputAssembly(new VulkanInputAssemblyBuilder().Configure(false, PrimitiveTopology.TriangleList))
                .WithViewportState(new VulkanViewportStateInfoBuilder()
                    .AddViewport(new Viewport(0, 0, swapchainExtent.Width, swapchainExtent.Height, 0, 1))
                    .AddScissors(new Rect2D(new Offset2D(), swapchainExtent)))
                .WithRasterizer(new VulkanRasterizerBuilder().CullFace(CullModeFlags.None))
                .WithMultisampleState(new VulkanMultisampleStateInfoBuilder().Configure(false, SampleCountFlags.Count1Bit))
                .WithColorBlendState(new VulkanColorBlendStateBuilder()
                    .AddAttachment(new PipelineColorBlendAttachmentState
                    {
                        BlendEnable = true,
                        SrcColorBlendFactor = BlendFactor.SrcAlpha,
                        DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                        ColorBlendOp = BlendOp.Add,
                        SrcAlphaBlendFactor = BlendFactor.One,
                        DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                        AlphaBlendOp = BlendOp.Add,
                        ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                         ColorComponentFlags.BBit | ColorComponentFlags.ABit
                    }))
                .AddDepthStencilState(new PipelineDepthStencilStateCreateInfo
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = false,
                    DepthWriteEnable = false,
                    DepthCompareOp = CompareOp.Always,
                    StencilTestEnable = false
                })
                .WithPipelineLayout(pipelineLayout)
                .AddRenderPass(IoC.Container.GetInstance<RenderPassManager>().GetRenderPass<DeferredPassStrategy>());

            var textPipeline = _pipelineManager.Create(textPipelineBuilder)!;

            // ---------- Materials ----------
            _imageMaterial = new Material("UI_Image");
            _imagePass = new MaterialPass(imagePipeline);
            _imageMaterial.AddPass("ui_image", _imagePass);

            _textMaterial = new Material("UI_Text");
            _textPass = new MaterialPass(textPipeline);
            _textMaterial.AddPass("ui_text", _textPass);
        }

        public void Execute(UploadBatch cmd, params object[] args)
        {
            uint frameIndex = (uint)args[0];
            var camera = args[1] as Camera ?? throw new ArgumentNullException(nameof(Camera));

            _uiManager.CollectFromWorld();
            _uiManager.RebuildBuffers();

            var viewport = camera.RenderTarget.Viewport;
            var scissor = camera.RenderTarget.Scissor;
            cmd.SetViewport(viewport);
            cmd.SetScissor(scissor);

            Matrix4x4 projection = Matrix4x4.CreateOrthographicOffCenter(
                0, viewport.Width, viewport.Height, 0, -1, 1);

            // Draw images
            if (_uiManager.ImageIndexCount > 0)
            {
                _uiManager.RecordDrawCommands(cmd, _imageMaterial!, _imagePass!, frameIndex, projection,
                    _uiManager.ImageIndexCount, 0);
            }

            // Draw texts
            if (_uiManager.TextIndexCount > 0)
            {
                _uiManager.RecordDrawCommands(cmd, _textMaterial!, _textPass!, frameIndex, projection,
                    _uiManager.TextIndexCount, _uiManager.TextFirstIndex);
            }
        }

        public void SetupAttachmentDescriptions(RenderPassBuilder builder)
        {
            // No new attachment – the swapchain color attachment is already defined by the lighting pass.
            // We only reference it in the subpass description.
        }

        public void SetupSubpassDescription(RenderPassBuilder.SubpassConfigurer subpass)
        {
            // The swapchain color attachment is at index 4 (after 3 gbuffer + 1 depth input).
            subpass.AddColorAttachment(4, ImageLayout.ColorAttachmentOptimal);
        }

        public void SetupDependencies(RenderPassBuilder builder, uint subpassIndex)
        {
            // Ensure lighting pass finishes writing before UI reads/writes to the color attachment.
            builder.AddDependency()
                .FromSubpass(LightingPass.Order) // subpass 1
                .ToSubpass(Order)                // subpass 2
                .WithStages(
                    PipelineStageFlags.ColorAttachmentOutputBit,
                    PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.FragmentShaderBit)
                .WithAccess(
                    AccessFlags.ColorAttachmentWriteBit,
                    AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit)
                .Add();
        }

        public void Dispose()
        {
            _imageMaterial?.Dispose();
            _textMaterial?.Dispose();
        }
    }
}