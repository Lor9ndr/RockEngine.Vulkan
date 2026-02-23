using RockEngine.Core.Builders;
using RockEngine.Core.DI;
using RockEngine.Core.Diagnostics;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Passes.SubPasses
{
    public class LightingPass : IRenderSubPass
    {
        private readonly VulkanContext _context;
        private readonly BindingManager _bindingManager;
        private readonly LightManager _lightManager;
        private readonly GraphicsContext _graphicsEngine;
        private readonly ShadowManager _shadowManager;
        private readonly PipelineManager _pipelineManager;
        private readonly GlobalUbo _globalUbo;
        private TextureBinding _iblBinding;
        private VkPipeline _lightingPipeline;

        public LightingPass(
            VulkanContext context,
            BindingManager bindingManager,
            LightManager lightManager,
            GraphicsContext graphicsEngine,
            ShadowManager shadowManager,
            PipelineManager pipelineManager,
            GlobalUbo globalUbo)
        {
            _context = context;
            _bindingManager = bindingManager;
            _lightManager = lightManager;
            _graphicsEngine = graphicsEngine;
            _shadowManager = shadowManager;
            _pipelineManager = pipelineManager;
            _globalUbo = globalUbo;
        }

        public static uint Order => 1;

        public static string Name => "light";

        public SubPassMetadata GetMetadata()
        {
            return new(Order, Name);
        }

        public void Initilize()
        {
            var shaderManager = IoC.Container.GetInstance<IShaderManager>();
            var vertShader = VkShaderModule.Create(_context, shaderManager.GetShader("deferred_lighting.vert"), ShaderStageFlags.VertexBit);
            var fragShader = VkShaderModule.Create(_context, shaderManager.GetShader("deferred_lighting.frag"), ShaderStageFlags.FragmentBit);

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
                    .AddViewport(new Viewport(0, 0, _graphicsEngine.MainSwapchain.Extent.Width,
                                             _graphicsEngine.MainSwapchain.Extent.Height, 0, 1))
                    .AddScissors(new Rect2D(new Offset2D(), _graphicsEngine.MainSwapchain.Extent)))
                .WithRasterizer(new VulkanRasterizerBuilder().CullFace(CullModeFlags.None))
                .WithMultisampleState(new VulkanMultisampleStateInfoBuilder().Configure(false, SampleCountFlags.Count1Bit))
                .WithColorBlendState(new VulkanColorBlendStateBuilder().AddAttachment(colorBlendAttachments))
                .AddRenderPass<DeferredPassStrategy>(IoC.Container.GetInstance<RenderPassManager>())
                .WithSubpass<LightingPass>()
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
            _lightingPipeline = _pipelineManager.Create(pipelineBuilder)!;
            SetIBLTextures(Texture3D.GetDefaultCubemapTexture(_context), Texture3D.GetDefaultCubemapTexture(_context), Texture2D.GetEmptyTexture(_context));
        }

        public void Execute(UploadBatch cmd, params object[] args)
        {
            using (PerformanceTracer.BeginSection(nameof(LightingPass)))
            {
                uint frameIndex = (uint)args[0];
                var camera = args[1] as Camera ?? throw new ArgumentNullException(nameof(Camera));
                var cameraIndex = args[2] as int? ?? throw new ArgumentNullException(nameof(Camera));

                cmd.SetViewport(camera.RenderTarget.Viewport);
                cmd.SetScissor(camera.RenderTarget.Scissor);
                if (camera.RenderTarget is CameraRenderTarget cameraRenderTarget)
                {
                    cmd.BindPipeline(_lightingPipeline, PipelineBindPoint.Graphics);


                    var materialPass = cameraRenderTarget.Material.GetPass(Name);
                    materialPass.BindResource(_lightManager.GetCurrentLightBufferBinding(frameIndex));
                    materialPass.BindResource(_lightManager.GetCountLightBufferBinding());
                    materialPass.BindResource(_shadowManager.GetShadowMapsBinding());
                    materialPass.BindResource(_shadowManager.GetPointShadowMapsBinding());
                    materialPass.BindResource(_globalUbo.GetBinding((uint)cameraIndex));
                    if (materialPass.Bindings.MaxSetLocation != 5)
                    {
                        var csm = _shadowManager.GetCSMDataBinding().Clone();
                        csm.SetLocation = 5;
                        materialPass.BindResource(csm);
                    }
                   

                    if (_iblBinding != null)
                    {
                        materialPass.BindResource(_iblBinding);
                    }
                    //camera.RenderTarget.GBuffer.Material.Bind(_binding);

                    materialPass.CmdPushConstants(cmd);
                    _bindingManager.BindResourcesForMaterial(frameIndex, materialPass, cmd);
                    cmd.Draw(3, 1, 0, 0);
                }
            }
        }

        internal void SetIBLTextures(Texture irradiance, Texture prefilter, Texture brdfLUT)
        {
            _iblBinding =  new TextureBinding(3, 0,  0, 1, ImageLayout.ShaderReadOnlyOptimal,irradiance, prefilter, brdfLUT);
        }

        public void SetupAttachmentDescriptions(RenderPassBuilder builder)
        {
            // Swapchain Color Attachment
            builder.ConfigureAttachment(_graphicsEngine.MainSwapchain.Format)
                .WithColorOperations(
                    load: AttachmentLoadOp.Clear,
                    store: AttachmentStoreOp.Store,
                    initialLayout: ImageLayout.ColorAttachmentOptimal,
                    finalLayout: ImageLayout.ColorAttachmentOptimal)
                .Add();
        }

        public void SetupSubpassDescription(RenderPassBuilder.SubpassConfigurer subpass)
        {
            int attachmentIndex = 0;

            // Input attachments (GBuffer)
            for (; attachmentIndex < GBuffer.ColorAttachmentFormats.Length; attachmentIndex++)
            {
                subpass.AddInputAttachment(attachmentIndex, ImageLayout.ShaderReadOnlyOptimal);
            }

            // Depth input attachment
            subpass.AddInputAttachment(
                attachmentIndex++,
                ImageLayout.DepthStencilReadOnlyOptimal);

            // Color attachment (Swapchain)
            subpass.AddColorAttachment(
                attachmentIndex,
                ImageLayout.ColorAttachmentOptimal);
        }

        public void SetupDependencies(RenderPassBuilder builder, uint subpassIndex)
        {
            // GeometryPass -> LightingPass dependency
            if (subpassIndex == 1)
            {
                builder.AddDependency()
                    .FromSubpass(subpassIndex - 1)
                    .ToSubpass(subpassIndex)
                    .WithStages(
                        PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.LateFragmentTestsBit,
                        PipelineStageFlags.FragmentShaderBit)
                    .WithAccess(
                        AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
                        AccessFlags.ShaderReadBit)
                    .Add();
            }
        }

        public void Dispose()
        {
        }

       
    }
}
