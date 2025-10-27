using NLog;

using RockEngine.Core.Builders;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Core.Rendering.Passes.SubPasses;
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


        private readonly IRenderPassStrategy[] _renderPassStrategies;
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

        private readonly Dictionary<MeshRenderer, int> _meshTransformIndices = new Dictionary<MeshRenderer, int>();
        private readonly Dictionary<MeshRenderer, Action<Transform>> _meshTransformHandlers = new Dictionary<MeshRenderer, Action<Transform>>();

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();


        public SwapchainRenderTarget SwapchainTarget { get; }

        public uint FrameIndex => _graphicsEngine.FrameIndex;

        public const ulong MAX_LIGHTS_SUPPORTED = 10_000;
        private const uint MAX_CAMERAS_SUPPORTED = 10;

        public GlobalUbo GlobalUbo { get; }

        public LightManager LightManager => _lightManager;

        public PipelineManager PipelineManager =>_pipelineManager;

        public BindingManager BindingManager => _bindingManager;
        public SubmitContext SubmitContext => _context.GraphicsSubmitContext;

        public CameraManager CameraManager => _cameraManager;

        public VulkanContext Context => _context;

        public GraphicsEngine GraphicsEngine => _graphicsEngine;

        public IBLManager IBLManager => _iblManager;
        public RckRenderPass RenderPass { get; private set; }


        public Renderer(VulkanContext context,
                        GraphicsEngine graphicsEngine,
                        PipelineManager pipelineManager,
                        IEnumerable<IRenderPassStrategy> renderPassStrategies,
                        BindingManager bindingManager,
                        TransformManager transformManager,
                        IndirectCommandManager indirectCommandManager,
                        RenderPassManager renderPassManager,
                        LightManager lightManager,
                        CameraManager cameraManager, 
                        GlobalUbo globalUbo)
        {
            _context = context;
            _graphicsEngine = graphicsEngine;
            _pipelineManager = pipelineManager;
            _renderPassStrategies = renderPassStrategies.OrderBy(s => s.Order).ToArray();
            _cameraManager = cameraManager;
            GlobalUbo = globalUbo;
            _bindingManager = bindingManager;
            _transformManager = transformManager;
            _lightManager = lightManager;
            _indirectCommandManager = indirectCommandManager;
            _renderPassManager = renderPassManager;
            SwapchainTarget = new SwapchainRenderTarget(context, graphicsEngine.Swapchain);
          
            _iblManager = new IBLManager(
           context,
           new ComputeShaderManager(context,  _pipelineManager),
           _bindingManager
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
        }


        public async Task Render()
        {
            using (PerformanceTracer.BeginSection("Frame Render"))
            {
                var swapchainPass = _renderPassStrategies.OfType<SwapchainPassStrategy>().First();
                var tasks = new List<Task>(_renderPassStrategies.Length);
                var lst = _renderPassStrategies.OrderBy(s => s.Order).ToList();
                for (int i = 0; i < lst.Count; i++)
                {
                    IRenderPassStrategy? item = lst[i];
                    await item.Execute(SubmitContext, _cameraManager, this);
                }
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
            var cameras = _cameraManager.RegisteredCameras;
            await _lightManager.UpdateAsync(cameras).ConfigureAwait(false);
            await _transformManager.UpdateAsync(FrameIndex).ConfigureAwait(false);
            await _indirectCommandManager.UpdateAsync().ConfigureAwait(false);

            foreach (var item in _renderPassStrategies)
            {
                await item.Update().ConfigureAwait(false);
            }

            if(cameras.Count > 0)
            {
               await GlobalUbo.UpdateAsync(cameras
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

            var transformIndex = _transformManager.AddTransform(mesh.Entity.Transform.WorldMatrix);

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
                return; // Not being drawn
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

        // Add a method to clean up all meshes
        public void CleanupAllMeshes()
        {
            var meshes = _meshTransformIndices.Keys.ToList();
            foreach (var mesh in meshes)
            {
                StopDrawing(mesh);
            }
            _meshTransformIndices.Clear();
            _meshTransformHandlers.Clear();
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
