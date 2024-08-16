using ImGuiNET;

using RockEngine.Vulkan.Assets;
using RockEngine.Vulkan.DI;
using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.Rendering;
using RockEngine.Vulkan.Rendering.ImGuiRender;
using RockEngine.Vulkan.Rendering.MaterialRendering;
using RockEngine.Vulkan.VkBuilders;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Input;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl;

using System.Numerics;
using System.Threading;

using Window = Silk.NET.Windowing.Window;

namespace RockEngine.Vulkan
{
    public class Application : IDisposable
    {
        private readonly Vk _api = Vk.GetApi();
        private PipelineManager _pipelineManager;
        private IWindow _window;
        private VulkanContext _context;
        private Project _project;
        private AssetManager _assetManager;
        private AssimpLoader _assimp;
        private IInputContext _inputContext;
        private BaseRenderer _baseRenderer;
        private SceneRenderSystem _sceneRenderSystem;
        private ImGuiController _imguiController;

        public CancellationTokenSource CancellationTokenSource { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Application()
        {
            CancellationTokenSource = new CancellationTokenSource();
            CancellationToken = CancellationTokenSource.Token;
        }

        public async Task RunAsync()
        {
            IoC.Register();
            SdlWindowing.Use();
            _window = Window.Create(WindowOptions.DefaultVulkan);
            _window.Title = "RockEngine";

            _window.Load += async () => await InitializeAsync()
                    .ConfigureAwait(false);

            try
            {
                await Task.Run(() => _window.Run(), CancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation
            }
            catch (Exception)
            {
                throw;
            }
        }

        private async Task InitializeAsync()
        {
            _inputContext = _window.CreateInput();
            
            IoC.Container.RegisterInstance(_inputContext);

            _context = new VulkanContext(_window, "Lor9ndr");

            _baseRenderer = new BaseRenderer(_context, _context.Surface);
            _assetManager = new AssetManager();

            _assimp = IoC.Container.GetInstance<AssimpLoader>();
            _pipelineManager = IoC.Container.GetInstance<PipelineManager>();

            InitializeRenderSystems();
            await LoadShaderPasses();
            await LoadProjectAsync();
            await InitializeSceneAsync();

            _window.Update += Update;
            _window.Render += async (s) => await DrawFrame(s)
                .ConfigureAwait(false);
        }

        private async Task LoadProjectAsync()
        {
            _project = await _assetManager.CreateProjectAsync("Sandbox","..\\Sandbox.asset", CancellationToken);
            var scene = _project.Scenes[0];
            await _assetManager.AddAssetToProject(_project,scene, CancellationToken);
            _window.Title = _project.Name;
        }

        private void InitializeRenderSystems()
        {
            _sceneRenderSystem = new SceneRenderSystem(_context);

            _imguiController = new ImGuiController(
                _context,
                _window,
                _inputContext,
                _context.Device.PhysicalDevice,
                _context.Device.QueueFamilyIndices.GraphicsFamily.Value,
                _baseRenderer.Swapchain.Images.Length,
                _baseRenderer.Swapchain.Format,
                _baseRenderer.Swapchain.DepthFormat,
                _baseRenderer.GetRenderPass());
        }

        private async Task InitializeSceneAsync()
        {
            var scene = _project.Scenes[0];
            var camera = new Entity();
            camera.AddComponent<DebugCamera>();
            await scene.AddEntityAsync(camera);

            var sponza = await _assimp.LoadMeshesAsync("F:\\RockEngine.Vulkan\\RockEngine.Vulkan\\Resources\\Models\\SponzaAtrium\\scene.gltf");
            var defaultEffect = _pipelineManager.GetEffect("ForwardDefault");
            var normalsEffect = _pipelineManager.GetEffect("Normals");
            for (int i = 0; i < sponza.Count; i++)
            {
                var entity = new Entity();
                var mesh = entity.AddComponent<MeshComponent>();
                var meshData = sponza[i];
                var meshAsset = new MeshAsset(meshData);
                mesh.SetAsset(meshAsset);
                var material = entity.AddComponent<MaterialComponent>();
                if (i % 2 == 0)
                {
                    material.Material = new Material(defaultEffect, meshData.textures, new Dictionary<string, object>());
                }
                else
                {
                    material.Material = new Material(normalsEffect, new List<Texture>(), new Dictionary<string, object>());
                }

                entity.Transform.Scale = new Vector3(0.005f);
                await scene.AddEntityAsync(entity);
            }

            await _assetManager.SaveAssetAsync(scene, CancellationToken);
            await scene.InitializeAsync();
        }

        private async Task LoadShaderPasses()
        {
            // Load the vertex shader
            var vertexShaderModule = await ShaderModuleWrapper.CreateAsync(_context, "..\\..\\..\\Resources\\Shaders\\Shader.vert.spv", ShaderStageFlags.VertexBit, CancellationToken)
                .ConfigureAwait(false);

            // Load the fragment shader
            var fragmentShaderModule = await ShaderModuleWrapper.CreateAsync(_context, "..\\..\\..\\Resources\\Shaders\\Shader.frag.spv", ShaderStageFlags.FragmentBit, CancellationToken)
                .ConfigureAwait(false);

            EffectTemplate effectTemplate = new EffectTemplate();

            var pipelineLayout = PipelineLayoutWrapper.Create(_context, true,vertexShaderModule, fragmentShaderModule);

            PipelineColorBlendAttachmentState colorBlendAttachmentState = new PipelineColorBlendAttachmentState()
            {
                ColorWriteMask = ColorComponentFlags.RBit |
               ColorComponentFlags.GBit |
               ColorComponentFlags.BBit |
               ColorComponentFlags.ABit
            };
            using GraphicsPipelineBuilder pBuilder = new GraphicsPipelineBuilder(_context,_pipelineManager, "Base")
               .AddRenderPass(_baseRenderer.GetRenderPass())
               .WithPipelineLayout(pipelineLayout)
               .WithDynamicState(new PipelineDynamicStateBuilder()
                   .AddState(DynamicState.Viewport)
                   .AddState(DynamicState.Scissor))
               .WithMultisampleState(new VulkanMultisampleStateInfoBuilder()
                   .Configure(false, SampleCountFlags.Count1Bit))
               .WithRasterizer(new VulkanRasterizerBuilder())
               .WithColorBlendState(new VulkanColorBlendStateBuilder()
                   .Configure(LogicOp.Copy)
                   .AddAttachment(colorBlendAttachmentState))
               .WithInputAssembly(new VulkanInputAssemblyBuilder().Configure())
               .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder()
                   .Add(Vertex.GetBindingDescription(), Vertex.GetAttributeDescriptions()))
               .WithShaderModule(vertexShaderModule)
               .WithShaderModule(fragmentShaderModule)
               .WithViewportState(new VulkanViewportStateInfoBuilder()
                   .AddViewport(new Viewport())
                   .AddScissors(new Rect2D()))
               .AddDepthStencilState(new PipelineDepthStencilStateCreateInfo()
               {
                   SType = StructureType.PipelineDepthStencilStateCreateInfo,
                   DepthTestEnable = true,
                   DepthWriteEnable = true,
                   DepthCompareOp = CompareOp.Less,
                   DepthBoundsTestEnable = false,
                   MinDepthBounds = 0.0f,
                   MaxDepthBounds = 1.0f,
                   StencilTestEnable = false,
                   Front = new StencilOpState(),
                   Back = new StencilOpState()
               });
            var pipeline = pBuilder.Build();

            ShaderEffect shaderEffect = new ShaderEffect([vertexShaderModule, fragmentShaderModule]);

            ShaderPass shaderPass = new ShaderPass()
            {
                Effect = shaderEffect,
                Layout = pipelineLayout,
                Pipeline = pipeline
            };
            effectTemplate.AddShaderPass(MeshpassType.Forward, shaderPass);
           _pipelineManager.AddEffectTemplate("ForwardDefault", effectTemplate);

            await LoadNormalEffect();
         
        }
        private async Task LoadNormalEffect()
        {
            // Load the vertex shader
            var vertexShaderModule = await ShaderModuleWrapper.CreateAsync(_context, "..\\..\\..\\Resources\\Shaders\\NormalsOnly.vert.spv", ShaderStageFlags.VertexBit, CancellationToken)
                .ConfigureAwait(false);

            // Load the fragment shader
            var fragmentShaderModule = await ShaderModuleWrapper.CreateAsync(_context, "..\\..\\..\\Resources\\Shaders\\NormalsOnly.frag.spv", ShaderStageFlags.FragmentBit, CancellationToken)
                .ConfigureAwait(false);

            EffectTemplate effectTemplate = new EffectTemplate();

            var pipelineLayout = PipelineLayoutWrapper.Create(_context, true, vertexShaderModule, fragmentShaderModule);

            PipelineColorBlendAttachmentState colorBlendAttachmentState = new PipelineColorBlendAttachmentState()
            {
                ColorWriteMask = ColorComponentFlags.RBit |
               ColorComponentFlags.GBit |
               ColorComponentFlags.BBit |
               ColorComponentFlags.ABit
            };

            using GraphicsPipelineBuilder pBuilder = new GraphicsPipelineBuilder(_context, _pipelineManager, "Base")
               .AddRenderPass(_baseRenderer.GetRenderPass())
               .WithPipelineLayout(pipelineLayout)
               .WithDynamicState(new PipelineDynamicStateBuilder()
                   .AddState(DynamicState.Viewport)
                   .AddState(DynamicState.Scissor))
               .WithMultisampleState(new VulkanMultisampleStateInfoBuilder()
                   .Configure(false, SampleCountFlags.Count1Bit))
               .WithRasterizer(new VulkanRasterizerBuilder())
               .WithColorBlendState(new VulkanColorBlendStateBuilder()
                   .Configure(LogicOp.Copy)
                   .AddAttachment(colorBlendAttachmentState))
               .WithInputAssembly(new VulkanInputAssemblyBuilder().Configure())
               .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder()
                   .Add(Vertex.GetBindingDescription(), Vertex.GetAttributeDescriptions()))
               .WithShaderModule(vertexShaderModule)
               .WithShaderModule(fragmentShaderModule)
               .WithViewportState(new VulkanViewportStateInfoBuilder()
                   .AddViewport(new Viewport())
                   .AddScissors(new Rect2D()))
               .AddDepthStencilState(new PipelineDepthStencilStateCreateInfo()
               {
                   SType = StructureType.PipelineDepthStencilStateCreateInfo,
                   DepthTestEnable = true,
                   DepthWriteEnable = true,
                   DepthCompareOp = CompareOp.Less,
                   DepthBoundsTestEnable = false,
                   MinDepthBounds = 0.0f,
                   MaxDepthBounds = 1.0f,
                   StencilTestEnable = false,
                   Front = new StencilOpState(),
                   Back = new StencilOpState()
               });
            var pipeline = pBuilder.Build();

            ShaderEffect shaderEffect = new ShaderEffect([vertexShaderModule, fragmentShaderModule]);

            ShaderPass shaderPass = new ShaderPass()
            {
                Effect = shaderEffect,
                Layout = pipelineLayout,
                Pipeline = pipeline
            };
            effectTemplate.AddShaderPass(MeshpassType.Forward, shaderPass);
            _pipelineManager.AddEffectTemplate("Normals", effectTemplate);
        }

        private async Task DrawFrame(double obj)
        {
            var frameInfo =  _baseRenderer.BeginFrame();
           
            if (frameInfo.CommandBuffer is null)
            {
                return;
            }

            frameInfo.FrameTime = (float)obj;


            _imguiController.Update((float)obj);
            ImGui.ShowDemoWindow();

            _baseRenderer.BeginSwapchainRenderPass(frameInfo.CommandBuffer);

            await _sceneRenderSystem.RenderAsync(_project, frameInfo).ConfigureAwait(false);

            _imguiController.RenderAsync(frameInfo.CommandBuffer, _baseRenderer.Swapchain.Extent);

            _baseRenderer.EndSwapchainRenderPass(frameInfo.CommandBuffer);
            _baseRenderer.EndFrame();
        }

        private void Update(double time)
        {
            _project.CurrentScene.Update(time);
        }

        /// <summary>
        /// Disposing all disposable objects
        /// </summary>
        public void Dispose()
        {
            CancellationTokenSource.Cancel();
            _api.DeviceWaitIdle(_context.Device);
            _sceneRenderSystem.Dispose();
            _baseRenderer.Dispose();
            IoC.Container.Dispose();
            _project.Dispose();
            _inputContext.Dispose();
            _context.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
