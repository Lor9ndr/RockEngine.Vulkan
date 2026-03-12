using NLog;

using RockEngine.Core.Builders;
using RockEngine.Core.Diagnostics;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Numerics;

using ZLinq;

namespace RockEngine.Core.Rendering
{
    public class WorldRenderer : IDisposable
    {
        private readonly VulkanContext _context;


        private readonly IRenderPassStrategy[] _renderPassStrategies;
        private readonly IBLManager _iblManager;
        private readonly LightManager _lightManager;
        private readonly TransformManager _transformManager;
        private readonly CameraManager _cameraManager;
        private readonly ShadowManager _shadowManager;
        private readonly IndirectCommandManager _indirectCommandManager;
        private readonly RenderPassManager _renderPassManager;
        private readonly BindingManager _bindingManager;
        private readonly GraphicsContext _graphicsEngine;
        private readonly PipelineManager _pipelineManager;
        private VkPipeline _skyboxPipeline;
        private uint _prevFrameIndex = uint.MaxValue;

        private readonly Dictionary<MeshRenderer, int> _meshTransformIndices = new Dictionary<MeshRenderer, int>();
        private readonly Dictionary<MeshRenderer, Action<Transform>> _meshTransformHandlers = new Dictionary<MeshRenderer, Action<Transform>>();

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();


        public SwapchainRenderTarget SwapchainTarget { get; }

        public uint FrameIndex => _graphicsEngine.FrameIndex;

        public const ulong MAX_LIGHTS_SUPPORTED = 10_000;

        public GlobalUbo GlobalUbo { get; }

        public LightManager LightManager => _lightManager;

        public PipelineManager PipelineManager =>_pipelineManager;

        public BindingManager BindingManager => _bindingManager;
        public SubmitContext SubmitContext => _context.GraphicsSubmitContext;

        public CameraManager CameraManager => _cameraManager;

        public VulkanContext Context => _context;

        public GraphicsContext GraphicsEngine => _graphicsEngine;

        public IBLManager IBLManager => _iblManager;
        public RckRenderPass RenderPass { get; private set; }

        public WorldRenderer(VulkanContext context,
                        GraphicsContext graphicsEngine,
                        PipelineManager pipelineManager,
                        IEnumerable<IRenderPassStrategy> renderPassStrategies,
                        BindingManager bindingManager,
                        TransformManager transformManager,
                        IndirectCommandManager indirectCommandManager,
                        RenderPassManager renderPassManager,
                        LightManager lightManager,
                        CameraManager cameraManager,
                        ShadowManager shadowManager,
                        GlobalUbo globalUbo)
        {
            _context = context;
            _graphicsEngine = graphicsEngine;
            _pipelineManager = pipelineManager;
            _renderPassStrategies = renderPassStrategies.OrderBy(s => s.Order).ToArray();
            _cameraManager = cameraManager;
            _shadowManager = shadowManager;
            GlobalUbo = globalUbo;
            _bindingManager = bindingManager;
            _transformManager = transformManager;
            _lightManager = lightManager;
            _indirectCommandManager = indirectCommandManager;
            _renderPassManager = renderPassManager;
            SwapchainTarget = new SwapchainRenderTarget(context, graphicsEngine.MainSwapchain);
          
            _iblManager = new IBLManager(
           context,
           new ComputeShaderManager(context,  _pipelineManager),
           _bindingManager
           //vulkanSynchronizationContext
            );
        }

        internal async Task InitializeAsync()
        {
            var iblManagerInitalizeTask = _iblManager.InitializeAsync();

            foreach (var item in _renderPassStrategies)
            {
                _renderPassManager.Register(item.BuildRenderPass(), item.GetType());

                item.InitializeSubPasses();
            }
            RenderPass = _renderPassManager.GetRenderPass<DeferredPassStrategy>();
            SwapchainTarget.Initialize(_renderPassManager.GetRenderPass<SwapchainPassStrategy>());

            _skyboxPipeline = CreateSkyboxPipeline();
            await iblManagerInitalizeTask;
            //await PhysicsManager.InitializeAsync();
        }


        public async Task Render(RenderContext renderContext)
        {
            using (PerformanceTracer.BeginSection("Frame Render"))
            {
                foreach (IRenderPassStrategy? item in _renderPassStrategies)
                {
                    await item.Execute(renderContext, this)
                        .ConfigureAwait(false);
                }
            }
        }

        public async ValueTask UpdateFrameData()
        {
            if (_prevFrameIndex == FrameIndex)
            {
                //return;
            }

            // Get shadow-casting lights before updates
            var shadowCastingLights = _lightManager.GetShadowCastingLights().ToList();

            // Update all frame data first
            var cameras = _cameraManager.RegisteredCameras;
            var updateTasks = new List<Task>
            {
                _lightManager.UpdateAsync(GraphicsEngine.FrameIndex).AsTask(),
                _transformManager.UpdateAsync(FrameIndex).AsTask(),
                _indirectCommandManager.UpdateAsync().AsTask(),
            };

            // Add render pass updates
            foreach (var item in _renderPassStrategies)
            {
                updateTasks.Add(item.Update().AsTask());
            }

            // Wait for all updates concurrently
            await Task.WhenAll(updateTasks).ConfigureAwait(false);


            if (cameras.Count > 0)
            {
                await GlobalUbo.UpdateAsync(cameras
                    .AsValueEnumerable()
                    .Select(s =>
                    {
                        bool succcess1 = Matrix4x4.Invert(s.ProjectionMatrix, out var invProj);
                        bool succcess2 = Matrix4x4.Invert(s.ViewMatrix, out var invView);
                        bool succcess = Matrix4x4.Invert(s.ViewProjectionMatrix, out var invViewProj);

                        return new GlobalUbo.GlobalUboData()
                        {

                            ViewProj = s.ViewProjectionMatrix,
                            InvProj = invProj,
                            InvView = invView,
                            Proj = s.ProjectionMatrix,
                            View = s.ViewMatrix,
                            InvViewProj = invViewProj,
                            CamPos = s.Entity.Transform.Position.AsVector4(),
                            ScreenSize = new Vector2(s.RenderTarget.Viewport.Width, s.RenderTarget.Viewport.Height),
                            FarClip = s.FarClip
                        };
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
            using var pipelineBuilder = GraphicsPipelineBuilder.CreateDefault(_context, "Skybox", RenderPass,[vertShader, fragShader]);
            pipelineBuilder.WithColorBlendState(new VulkanColorBlendStateBuilder()
                    .AddAttachment(colorBlendAttachments))
                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder()
                     .Add(Vertex.GetBindingDescription(), Vertex.GetAttributeDescriptions()))
                .WithSubpass<PostLightPass>()
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
            if (_meshTransformIndices.ContainsKey(mesh))
            {
                _logger.Warn($"Mesh {mesh.Entity.Name} is already being drawn");
                return;
            }

            // Use the new method that automatically groups by material and mesh
            var transformIndex = _transformManager.AllocateTransformForMesh(
                mesh.Entity.Transform.WorldMatrix,
                mesh.Material,
                mesh.Mesh
            );

            // Create a properly captured event handler
            void transformHandler(Transform transform)
            {
                if (_transformManager.IsTransformActive(transformIndex))
                {
                    _transformManager.UpdateTransform(transformIndex, transform.WorldMatrix);
                }
            }

            mesh.Entity.Transform.TransformChanged += transformHandler;

            _indirectCommandManager.AddMesh(mesh, (uint)transformIndex);

            // Store the relationships
            _meshTransformIndices[mesh] = transformIndex;
            _meshTransformHandlers[mesh] = transformHandler;

            // Let the mesh know about its transform index and handler
            mesh.SetTransformIndex(transformIndex);
            mesh.SetTransformChangedHandler(transformHandler);
        }

        public void StopDrawing(MeshRenderer meshRenderer)
        {
            if (!_meshTransformIndices.TryGetValue(meshRenderer, out int transformIndex))
            {
                return;
            }

            // Remove from command manager first
            _indirectCommandManager.RemoveMesh(meshRenderer);

            // Remove transform event handler
            if (_meshTransformHandlers.TryGetValue(meshRenderer, out var handler))
            {
                if (meshRenderer.Entity?.Transform != null)
                {
                    meshRenderer.Entity.Transform.TransformChanged -= handler;
                }
                _meshTransformHandlers.Remove(meshRenderer);
            }

            // Remove the transform from manager
            _transformManager.RemoveTransform(transformIndex);

            // Clean up tracking
            _meshTransformIndices.Remove(meshRenderer);

            _logger.Info($"Stopped drawing mesh {meshRenderer.Entity.Name}, transform index {transformIndex} freed");
        }

        public void AddCommand(IRenderCommand command)
        {
            _indirectCommandManager.AddCommand(command);
        }

        public int RegisterCamera(Camera camera)
        {
            return _cameraManager.Register(camera, this);
        }

        public void Dispose()
        {
            foreach (var item in _renderPassStrategies)
            {
                item.Dispose();

            }
        }

        internal void UnRegisterCamera(Camera camera)
        {
            _cameraManager.Unregister(camera);
        }
    }
}
