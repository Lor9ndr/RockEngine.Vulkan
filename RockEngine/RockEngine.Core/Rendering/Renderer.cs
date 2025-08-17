using RockEngine.Core.Builders;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using ZLinq;

namespace RockEngine.Core.Rendering
{
    public class Renderer : IDisposable
    {
        private readonly VulkanContext _context;

        public EngineRenderPass RenderPass { get; private set; }

        private readonly IEnumerable<IRenderPassStrategy> _renderPassStrategies;
        private readonly IBLManager _iblManager;
        private readonly LightManager _lightManager;
        private readonly TransformManager _transformManager;
        private readonly CameraManager _cameraManager;
        private readonly IndirectCommandManager _indirectCommandManager;
        private readonly RenderPassManager _renderPassManager;
        private readonly BindingManager _bindingManager;
        private readonly GraphicsEngine _graphicsEngine;
        private readonly PipelineManager _pipelineManager;
        private VkPipeline _skyboxPipeline;
        private uint _prevFrameIndex = uint.MaxValue;


        public SwapchainRenderTarget SwapchainTarget { get; }

        public uint FrameIndex => (uint)_graphicsEngine.FrameIndex;

        public const ulong MAX_LIGHTS_SUPPORTED = 10_000;
        private const uint MAX_CAMERAS_SUPPORTED = 10;

        public GlobalUbo GlobalUbo { get; }

        public LightManager LightManager => _lightManager;

        public PipelineManager PipelineManager =>_pipelineManager;

        public BindingManager BindingManager => _bindingManager;
        public SubmitContext SubmitContext => _context.SubmitContext;

        public Renderer(VulkanContext context,
                        GraphicsEngine graphicsEngine,
                        PipelineManager pipelineManager,
                        IEnumerable<IRenderPassStrategy> renderPassStrategies,
                        BindingManager bindingManager,
                        TransformManager transformManager,
                        IndirectCommandManager indirectCommandManager,
                        RenderPassManager renderPassManager,
                        LightManager lightManager,
                        CameraManager cameraManager, GlobalUbo globalUbo)
        {
            _context = context;
            _graphicsEngine = graphicsEngine;
            _pipelineManager = pipelineManager;
            _renderPassStrategies = renderPassStrategies.OrderBy(s => s.Order);
            _cameraManager = cameraManager;
            GlobalUbo = globalUbo;
            _bindingManager = bindingManager;
            _transformManager = transformManager;
            _lightManager = lightManager;
            _renderPassStrategies = renderPassStrategies;
            _indirectCommandManager = indirectCommandManager;
            _renderPassManager = renderPassManager;
            SwapchainTarget = new SwapchainRenderTarget(context, graphicsEngine.Swapchain);
          
            _iblManager = new IBLManager(
           context,
           new ComputeShaderManager(context, _bindingManager, _pipelineManager),
           _bindingManager
            );
        }

        internal async Task InitializeAsync()
        {
            var iblManagerInitalizeTask = _iblManager.InitializeAsync();

            foreach (var item in _renderPassStrategies)
            {
                _renderPassManager.CreateRenderPass(item.BuildRenderPass(_graphicsEngine), item.GetType());

                item.InitializeSubPasses();
            }
            RenderPass = _renderPassManager.GetRenderPass<DeferredPassStrategy>();
            SwapchainTarget.Initialize(_renderPassManager.GetRenderPass<SwapchainPassStrategy>().RenderPass);

            _skyboxPipeline = CreateSkyboxPipeline();


            // Generate IBL textures after loading environment map
            var envMap = await Texture3D.CreateCubeMapAsync(_context, [
            "Resources/skybox/right.jpg",    // +X
            "Resources/skybox/left.jpg",     // -X
            "Resources/skybox/top.jpg",      // +Y (Vulkan's Y points down)
            "Resources/skybox/bottom.jpg",   // -Y
            "Resources/skybox/front.jpg",    // +Z
            "Resources/skybox/back.jpg"      // -Z
            ]).ConfigureAwait(true); ;

            envMap.Image.LabelObject("EnviromentMap");

            await iblManagerInitalizeTask;
            // Ожидаем генерацию всех IBL текстур
            var textures = await Task.WhenAll(
                _iblManager.GenerateIrradianceMap(envMap, 128),
                _iblManager.GeneratePrefilterMap(envMap, 512),
                _iblManager.GenerateBRDFLUT(512)
            ).ConfigureAwait(true);


            var irradiance = textures[0];
            var prefilter = textures[1];
            var brdfLUT = textures[2];

            irradiance.Image.LabelObject("Irradiance");
            prefilter.Image.LabelObject("Prefilter");
            brdfLUT.Image.LabelObject("BRDFLut");

            // Store references in lighting pass
            var lightingPass = _renderPassStrategies.OfType<DeferredPassStrategy>().First().LightingPass;
            lightingPass.SetIBLTextures(irradiance, prefilter, brdfLUT);
        }

       
        public  void Render(UploadBatch uploadBatch)
        {
            //CreateBarrier(uploadBatch.CommandBuffer);

            using (PerformanceTracer.BeginSection("Frame Render"))
            {
                foreach (var item in _renderPassStrategies)
                {
                    item.Execute(uploadBatch, _cameraManager, this);
                }
            }
        }

        private static void CreateBarrier(VkCommandBuffer primaryCmdBuffer)
        {
            var barrier = new MemoryBarrier
            {
                SType = StructureType.MemoryBarrier,
                SrcAccessMask = AccessFlags.MemoryWriteBit,
                DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit
            };
            unsafe
            {
                VulkanContext.Vk.CmdPipelineBarrier(
               primaryCmdBuffer,
               PipelineStageFlags.AllCommandsBit,
               PipelineStageFlags.AllCommandsBit,
               0,
               1,
               &barrier,
               0,
               null,
               0,
               null
           );
            }
        }

        public async ValueTask UpdateFrameData()
        {
            if(_prevFrameIndex == FrameIndex)
            {
                // Frame havent been rendered, then no update needed
                return;
            }

            // Update all frame data first
            await _lightManager.UpdateAsync(_cameraManager.ActiveCameras).ConfigureAwait(false);
            await _transformManager.UpdateAsync(FrameIndex).ConfigureAwait(false);
            await _indirectCommandManager.UpdateAsync().ConfigureAwait(false);

            foreach (var item in _renderPassStrategies)
            {
                await item.Update().ConfigureAwait(false);
            }

            if(_cameraManager.ActiveCameras.Count > 0)
            {
               await GlobalUbo.UpdateAsync(_cameraManager.ActiveCameras
                   .AsValueEnumerable()
                   .Select(s => new GlobalUbo.GlobalUboData()
                {
                    Position = s.Entity.Transform.Position,
                    ViewProjection = s.ViewProjectionMatrix,
                }).ToArray()).ConfigureAwait(false);
            }
            _prevFrameIndex = FrameIndex;
        }

      

        private unsafe VkPipeline CreateSkyboxPipeline()
        {
            var vertShader = VkShaderModule.Create(_context, "Shaders/Skybox.vert.spv", ShaderStageFlags.VertexBit);
            var fragShader = VkShaderModule.Create(_context, "Shaders/Skybox.frag.spv", ShaderStageFlags.FragmentBit);
            var colorBlendAttachments = new PipelineColorBlendAttachmentState[1]
              {
                    new PipelineColorBlendAttachmentState
                    {
                        ColorWriteMask = ColorComponentFlags.RBit |
                                        ColorComponentFlags.GBit |
                                        ColorComponentFlags.BBit |
                                        ColorComponentFlags.ABit,
                        BlendEnable = false
                    }
              };
            using var pipelineBuilder = GraphicsPipelineBuilder.CreateDefault(_context, "Skybox", [vertShader, fragShader]);
            pipelineBuilder.WithColorBlendState(new VulkanColorBlendStateBuilder()
                    .AddAttachment(colorBlendAttachments))
                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder()
                     .Add(Vertex.GetBindingDescription(), Vertex.GetAttributeDescriptions()))
                .AddRenderPass(RenderPass)
                .WithSubpass(2)
                .AddDepthStencilState(new PipelineDepthStencilStateCreateInfo
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = true,
                    DepthWriteEnable = false,
                    DepthCompareOp = CompareOp.LessOrEqual,
                    DepthBoundsTestEnable = false,
                    StencilTestEnable = false,
                });

            return _pipelineManager.Create(pipelineBuilder);
        }

        public void Draw(MeshRenderer mesh)
        {
            var transformIndex = _transformManager.AddTransform(mesh.Entity.Transform.WorldMatrix);
            _indirectCommandManager.AddMesh(mesh, transformIndex);
            mesh.Entity.Transform.TransformChanged += () =>
            {
                _transformManager.UpdateTransform(transformIndex,mesh.Entity.Transform.WorldMatrix);
            };
        }

        public void AddCommand(IRenderCommand command)
        {
            _indirectCommandManager.AddCommand(command);
        }

        public void RegisterCamera(Camera camera) => _cameraManager.Register(camera,this);
        public void Dispose()
        {
            foreach (var item in _renderPassStrategies)
            {
                item.Dispose();

            }
        }
    }
}
