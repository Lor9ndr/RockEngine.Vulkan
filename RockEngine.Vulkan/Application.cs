using ImGuiNET;

using RockEngine.Vulkan.Assets;
using RockEngine.Vulkan.DI;
using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.Rendering;
using RockEngine.Vulkan.Rendering.ImGuiRender;
using RockEngine.Vulkan.Rendering.MaterialRendering;
using RockEngine.Vulkan.Utils;
using RockEngine.Vulkan.VkBuilders;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Input;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl;

using System.Numerics;

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
        private FrameInfo FrameInfo = new FrameInfo();

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
            await LoadPipelines();
            await LoadProjectAsync();
            await InitializeSceneAsync();

            _window.Update += async (s) => await Update(s)
                .ConfigureAwait(false);

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
            var light = new Entity();
            light.Transform.Position = new Vector3(0, 10, 0);
            light.Transform.Scale = new Vector3(1, 1, 1);
            light.AddComponent<LightComponent>();
  
            var lightMesh = light.AddComponent<MeshComponent>();
            lightMesh.SetAsset(new MeshAsset(new MeshData("Cube", DefaultMesh.CubeVertices,Indices: null, null)));
            await scene.AddEntityAsync(light);

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
                mesh.SetMaterial(new Material(defaultEffect, meshData.textures, new Dictionary<string, object>()));

                entity.Transform.Scale = new Vector3(0.005f);
                await scene.AddEntityAsync(entity);
            }


            await _assetManager.SaveAssetAsync(scene, CancellationToken);
            await scene.InitializeAsync();
        }

        private async Task LoadPipelines()
        {
            // Load the vertex shader
            var vertexShaderModule = await ShaderModuleWrapper.CreateAsync(_context, "..\\..\\..\\Resources\\Shaders\\Shader.vert.spv", ShaderStageFlags.VertexBit, CancellationToken)
                .ConfigureAwait(false);

            // Load the fragment shader
            var fragmentShaderModule = await ShaderModuleWrapper.CreateAsync(_context, "..\\..\\..\\Resources\\Shaders\\Shader.frag.spv", ShaderStageFlags.FragmentBit, CancellationToken)
                .ConfigureAwait(false);

            EffectTemplate effectTemplate = new EffectTemplate();

            var pipelineLayout = PipelineLayoutWrapper.Create(_context, vertexShaderModule, fragmentShaderModule);

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
            await LoadColorLitEffect();
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

            var pipelineLayout = PipelineLayoutWrapper.Create(_context, vertexShaderModule, fragmentShaderModule);

            PipelineColorBlendAttachmentState colorBlendAttachmentState = new PipelineColorBlendAttachmentState()
            {
                ColorWriteMask = ColorComponentFlags.RBit |
               ColorComponentFlags.GBit |
               ColorComponentFlags.BBit |
               ColorComponentFlags.ABit
            };

            using GraphicsPipelineBuilder pBuilder = new GraphicsPipelineBuilder(_context, _pipelineManager, "Normals")
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
        private async Task LoadColorLitEffect()
        {
            // Load the vertex shader
            var vertexShaderModule = await ShaderModuleWrapper.CreateAsync(_context, "..\\..\\..\\Resources\\Shaders\\ColorLit.vert.spv", ShaderStageFlags.VertexBit, CancellationToken)
                .ConfigureAwait(false);

            // Load the fragment shader
            var fragmentShaderModule = await ShaderModuleWrapper.CreateAsync(_context, "..\\..\\..\\Resources\\Shaders\\ColorLit.frag.spv", ShaderStageFlags.FragmentBit, CancellationToken)
                .ConfigureAwait(false);

            EffectTemplate effectTemplate = new EffectTemplate();

            var pipelineLayout = PipelineLayoutWrapper.Create(_context, vertexShaderModule, fragmentShaderModule);

            PipelineColorBlendAttachmentState colorBlendAttachmentState = new PipelineColorBlendAttachmentState()
            {
                ColorWriteMask = ColorComponentFlags.RBit |
               ColorComponentFlags.GBit |
               ColorComponentFlags.BBit |
               ColorComponentFlags.ABit
            };

            using GraphicsPipelineBuilder pBuilder = new GraphicsPipelineBuilder(_context, _pipelineManager, "ColorLit")
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
            _pipelineManager.AddEffectTemplate("ColorLit", effectTemplate);
        }

        private async Task DrawFrame(double obj)
        {
            _baseRenderer.BeginFrame(FrameInfo);
           
            if (FrameInfo.CommandBuffer is null)
            {
                return;
            }

            FrameInfo.FrameTime = (float)obj;


            _imguiController.Update((float)obj);
            ImGui.ShowDemoWindow();
            var lightEntity = _project.CurrentScene.GetEntities().FirstOrDefault(s => s.GetComponent<LightComponent>() != null);
            if (ImGui.Begin("Light", ImGuiWindowFlags.UnsavedDocument | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse))
            {
                var pos = lightEntity.Transform.Position;
                if (ImGui.InputFloat3("Position", ref pos))
                {
                    lightEntity.Transform.Position = pos;
                }
            }
            _baseRenderer.BeginSwapchainRenderPass(FrameInfo.CommandBuffer);

            await _sceneRenderSystem.RenderAsync(_project, FrameInfo).ConfigureAwait(false);

            _imguiController.RenderAsync(FrameInfo.CommandBuffer, _baseRenderer.Swapchain.Extent);

            _baseRenderer.EndSwapchainRenderPass(FrameInfo.CommandBuffer);
            _baseRenderer.EndFrame();
        }

        private float _lightAngle = 0f;
        private const float _lightRadius = 10f;
        private const float _lightSpeed = 0.5f;
        private const float _colorChangeSpeed = 0.3f;

        private async Task Update(double time)
        {
            await _project.CurrentScene.UpdateAsync(time);

            // Update light position and color
            _lightAngle += _lightSpeed * (float)time;
            if (_lightAngle > 2 * MathF.PI)
            {
                _lightAngle -= 2 * MathF.PI;
            }

            var lightEntity = _project.CurrentScene.GetEntities().FirstOrDefault(s => s.GetComponent<LightComponent>() != null);
            if (lightEntity != null)
            {
                var lightComponent = lightEntity.GetComponent<LightComponent>();

                // Update position
                float x = _lightRadius * MathF.Cos(_lightAngle);
                float z = _lightRadius * MathF.Sin(_lightAngle);
                lightEntity.Transform.Position = new Vector3(x, 15f, z);

                // Update color
                float r = (MathF.Sin(_colorChangeSpeed * _lightAngle) ) / 2;
                float g = (MathF.Sin(_colorChangeSpeed * _lightAngle * MathF.PI / 3) + 1) / 2;
                float b = (MathF.Sin(_colorChangeSpeed * _lightAngle * MathF.PI / 2) + 1) / 2;
                lightComponent.Color = new Vector3(r, g, b);
            }
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
