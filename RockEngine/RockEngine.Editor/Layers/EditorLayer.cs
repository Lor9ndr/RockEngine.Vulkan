using ImGuiNET;

using NLog;

using RockEngine.Core;
using RockEngine.Core.Assets;
using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.Assets.RockEngine.Core.Assets;
using RockEngine.Core.Builders;
using RockEngine.Core.DI;
using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Editor.EditorComponents;
using RockEngine.Editor.EditorUI;
using RockEngine.Editor.EditorUI.EditorWindows;
using RockEngine.Editor.EditorUI.ImGuiRendering;
using RockEngine.Editor.EditorUI.Logging;
using RockEngine.Editor.UIAttributes;
using RockEngine.Vulkan;

using Silk.NET.Input;
using Silk.NET.Vulkan;

using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

using ZLinq;

namespace RockEngine.Editor.Layers
{
    public class EditorLayer : ILayer
    {
        private readonly World _world;
        private readonly VulkanContext _context;
        private readonly GraphicsEngine _graphicsEngine;
        private readonly Renderer _renderer;
        private readonly IInputContext _inputContext;
        private readonly AssetManager _assetManager;
        private readonly EditorConsole _editorConsole;
        private readonly EditorStateManager _stateManager;
        private readonly EditorDockSpace _dockSpace;
        private readonly MainMenuBar _mainMenuBar;
        private readonly Toolbar _toolbar;
        private readonly SceneHierarchyWindow _sceneHierarchy;
        private readonly InspectorWindow _inspector;
        private readonly ViewportWindow _sceneViewport;
        private readonly ViewportWindow _gameViewport;
        private readonly ConsoleWindow _console;
        private readonly PerformanceWindow _performance;
        private VkPipelineLayout _pipelineLayout;
        private VkPipeline _pipeline;


        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public EditorLayer(World world,
            VulkanContext context,
            GraphicsEngine graphicsEngine,
            Renderer renderer,
            IInputContext inputContext,
            AssetManager assetManager,
            EditorConsole editorConsole,
            ImGuiController imGuiController
            )
        {
            _world = world;
            _context = context;
            _graphicsEngine = graphicsEngine;
            _renderer = renderer;
            _inputContext = inputContext;
            _assetManager = assetManager;
            _editorConsole = editorConsole;
            _stateManager = new EditorStateManager();
            // Initialize UI components
            _dockSpace = new EditorDockSpace();
            _mainMenuBar = new MainMenuBar();
            _dockSpace = new EditorDockSpace();
            _mainMenuBar = new MainMenuBar();
            _toolbar = new Toolbar();
            _sceneHierarchy = new SceneHierarchyWindow(world);
            _inspector = new InspectorWindow(assetManager, imGuiController);
            _sceneViewport = new ViewportWindow("Scene View", world, inputContext, imGuiController);
            _gameViewport = new ViewportWindow("Game View", world, inputContext, imGuiController);
            _console = new ConsoleWindow(editorConsole);
            _performance = new PerformanceWindow();

            // Connect event handlers
            _sceneHierarchy.SelectedEntityChanged += (entity) => _inspector.SelectedEntity = entity;
            _mainMenuBar.ViewToggled += OnViewToggled;

            // Apply theme
            EditorTheme.ApplyModernDarkTheme();

            DefaultMeshes.Initalize(_assetManager);
        }

        private void OnViewToggled(string viewName, bool isVisible)
        {
            switch (viewName)
            {
                case "Scene Hierarchy":
                    _sceneHierarchy.IsOpen = isVisible;
                    break;
                case "Inspector":
                    _inspector.IsOpen = isVisible;
                    break;
                case "Console":
                    _console.IsOpen = isVisible;
                    break;
                case "Performance":
                    _performance.IsOpen = isVisible;
                    break;
                case "Material Templates":
                    // Handle material templates window visibility
                    break;
                case "Memory Stats":
                    // Handle memory stats window visibility  
                    break;
            }
        }

        private async Task LoadAssetsFromProject(string projectPath)
        {
            try
            {
                _logger.Info($"Loading assets from project: {projectPath}");

                // Load the project
                await _assetManager.LoadProjectAsync(projectPath);

                // Load the main scene
                var scene = await _assetManager.LoadAsync<SceneData>(new AssetPath("Scenes", "DebugScene"));
                await scene.LoadDataAsync();
                ((SceneAsset)scene).InstantiateEntities();

                _logger.Info("Project assets loaded successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load assets from project");
                throw;
            }
        }

        private async Task CreateAssetsProgrammatically()
        {
            try
            {
                _logger.Info("Creating assets programmatically");

                // Create project
                var project = await _assetManager.CreateProjectAsync("DebugProject",
                    "X:\\RockEngine.Vulkan\\RockEngine\\RockEngine.Editor\\bin\\Debug\\net9.0\\Project\\TestAssetSystem");

                // Create scene
                var scene = _assetManager.Create<SceneAsset>(new AssetPath("Scenes", "DebugScene"));
                scene.SetData(new SceneData());

                // Create skybox texture
                var skyboxAsset = await CreateTexture("Skybox", "debug_skybox", [
                    "Resources/skybox/right.jpg",
            "Resources/skybox/left.jpg",
            "Resources/skybox/top.jpg",
            "Resources/skybox/bottom.jpg",
            "Resources/skybox/front.jpg",
            "Resources/skybox/back.jpg"
                ], TextureType.TextureCube);

                // Get cube mesh
                var cubeMesh = _assetManager.GetAsset<MeshAsset>(DefaultMeshes.CubeAssetID);

                // Create scene entities
                await CreateSceneEntities(scene, cubeMesh, skyboxAsset);

                // Load model
                var sponza = await _assetManager.LoadModelAsync("Resources\\Models\\SponzaAtrium\\scene.gltf", "Sponza");
                CreateModelEntities(scene, sponza, new Vector3(0), new Vector3(0.1f));

                // Save everything
                await SaveAssetsAsync(sponza, skyboxAsset, scene);

                _logger.Info("Programmatic assets created successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to create assets programmatically");
                throw;
            }
        }
        private async Task<TextureAsset> CreateTexture(string folder, string name, string[] filePaths, TextureType type)
        {
            var texture = _assetManager.Create<TextureAsset>(new AssetPath(folder, name));
            texture.SetData(new TextureData
            {
                FilePaths = filePaths.ToList(),
                GenerateMipmaps = type != TextureType.TextureCube,
                Type = type
            });
            await texture.LoadGpuResourcesAsync();
            await _assetManager.SaveAsync(texture);
            return texture;
        }
        private async Task CreateSceneEntities(SceneAsset scene, MeshAsset cubeMesh, TextureAsset skyboxAsset)
        {
            // Skybox
            var skybox = scene.CreateEntity();
            skybox.AddComponent<Skybox>().Cubemap = skyboxAsset;
            skybox.Transform.Scale = new Vector3(100, 100, 100);

            // Camera
            var camera = scene.CreateEntity();
            camera.AddComponent<Camera>();
            camera.Name = "Camera";

            // Create 100 lights with solid materials
            for (int i = 0; i < 100; i++)
            {
                var lightColor = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());

                var solidMaterial = _assetManager.Create<MaterialAsset>(new AssetPath("Materials", $"solid{i}"));
                solidMaterial.SetData(new MaterialData
                {
                    PipelineName = "Solid",
                    Parameters = new Dictionary<string, object> { ["lightColor"] = lightColor }
                });

                var light = scene.CreateEntity();
                light.Layer = RenderLayerType.Solid;
                light.Name = $"light ({i})";
                light.Transform.Scale *= 0.5f;

                var lightComponent = light.AddComponent<Light>();
                lightComponent.Type = LightType.Point;
                light.Transform.Position = new Vector3(
                    Random.Shared.NextSingle() * 100,
                    20 + Random.Shared.NextSingle() * 100,
                    Random.Shared.NextSingle() * 100
                );

                var lightMeshRenderer = light.AddComponent<MeshRenderer>();
                lightMeshRenderer.SetAssets(cubeMesh, solidMaterial);
                lightComponent.Intensity = 100;
                lightComponent.Radius = 100;
                lightComponent.Color = lightColor;
                await _assetManager.SaveAsync(solidMaterial);
            }
        }
        private async Task SaveAssetsAsync(params IAsset[] assets)
        {
            foreach (var asset in assets)
            {
                await _assetManager.SaveAsync(asset);
            }
        }


        // Modify the OnAttach method to use these methods
        public async Task OnAttach()
        {
            CretePipeline();
            CreateSolidPipeline();

            // Choose between loading from project or creating programmatically
          /*  bool loadFromProject = false;

            if (loadFromProject)
            {
                await LoadAssetsFromProject(
                    @"X:\RockEngine.Vulkan\RockEngine\RockEngine.Editor\bin\Debug\net9.0\Project\TestAssetSystem\DebugProject\DebugProject.rockproj");
            }
            else
            {
                await CreateAssetsProgrammatically();
            }*/
        }
        private void CreateModelEntities(SceneAsset scene, ModelAsset model, Vector3 position, Vector3 scale, Quaternion? rotation = null)
        {
            var parent = scene.CreateEntity();
            parent.Name = model.Name;
            parent.Transform.Position = position;
            parent.Transform.Scale = scale;
            if (rotation.HasValue) parent.Transform.Rotation = rotation.Value;

            foreach (var part in model.Parts)
            {
                var entity = scene.CreateEntity();
                entity.AddComponent<MeshRenderer>().SetAssets(part.Mesh, part.Material);
                parent.AddChild(entity);
            }
        }

        private void CretePipeline()
        {
            var shaderManager = IoC.Container.GetInstance<IShaderManager>();
            VkShaderModule vkShaderModuleFrag = VkShaderModule.Create(_context, shaderManager.GetShader("Geometry.frag"), ShaderStageFlags.FragmentBit);

            VkShaderModule vkShaderModuleVert = VkShaderModule.Create(_context, shaderManager.GetShader("Geometry.vert"), ShaderStageFlags.VertexBit);

            _pipelineLayout = VkPipelineLayout.Create(_context, vkShaderModuleVert, vkShaderModuleFrag);

            var binding_desc = new VertexInputBindingDescription();
            binding_desc.Stride = (uint)Unsafe.SizeOf<Vertex>();
            binding_desc.InputRate = VertexInputRate.Vertex;

            var colorBlendAttachments = new PipelineColorBlendAttachmentState[GBuffer.ColorAttachmentFormats.Length];
            for (int i = 0; i < GBuffer.ColorAttachmentFormats.Length; i++)
            {
                colorBlendAttachments[i] = new PipelineColorBlendAttachmentState
                {
                    ColorWriteMask = ColorComponentFlags.RBit |
                                    ColorComponentFlags.GBit |
                                    ColorComponentFlags.BBit |
                                    ColorComponentFlags.ABit,
                    BlendEnable = false
                };
            }


            using GraphicsPipelineBuilder pipelineBuilder = new GraphicsPipelineBuilder(_context, "Geometry")
                 .WithShaderModule(vkShaderModuleVert)
                 .WithShaderModule(vkShaderModuleFrag)
                 .WithRasterizer(new VulkanRasterizerBuilder().CullFace(CullModeFlags.FrontBit).FrontFace(FrontFace.Clockwise))
                 .WithInputAssembly(new VulkanInputAssemblyBuilder().Configure())
                 .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder()
                     .Add(Vertex.GetBindingDescription(), Vertex.GetAttributeDescriptions()))
                 .WithViewportState(new VulkanViewportStateInfoBuilder()
                     .AddViewport(new Viewport() { Height = _graphicsEngine.Swapchain.Surface.Size.Y, Width = _graphicsEngine.Swapchain.Surface.Size.X })
                     .AddScissors(new Rect2D()
                     {
                         Offset = new Offset2D(),
                         Extent = new Extent2D((uint?)_graphicsEngine.Swapchain.Surface.Size.X, (uint?)_graphicsEngine.Swapchain.Surface.Size.Y)
                     }))
                 .WithMultisampleState(new VulkanMultisampleStateInfoBuilder().Configure(false, SampleCountFlags.Count1Bit))
                 .WithColorBlendState(new VulkanColorBlendStateBuilder()
                     .AddAttachment(colorBlendAttachments))
                 .AddRenderPass<DeferredPassStrategy>(IoC.Container.GetInstance<RenderPassManager>())
                 .WithPipelineLayout(_pipelineLayout)
                 .WithSubpass<GeometryPass>()
                 .WithDynamicState(new PipelineDynamicStateBuilder()
                    .AddState(DynamicState.Viewport)
                    .AddState(DynamicState.Scissor)
                    )
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
                 });
            _pipeline = _renderer.PipelineManager.Create(pipelineBuilder);

        }
        private void CreateSolidPipeline()
        {
            var shaderManager = IoC.Container.GetInstance<IShaderManager>();
            var vertShader = VkShaderModule.Create(_context, shaderManager.GetShader("Solid.vert"), ShaderStageFlags.VertexBit);
            var fragShader = VkShaderModule.Create(_context, shaderManager.GetShader("Solid.frag"), ShaderStageFlags.FragmentBit);
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
            using var pipelineBuilder = GraphicsPipelineBuilder.CreateDefault(_context, "Solid", [vertShader, fragShader]);
            pipelineBuilder.WithColorBlendState(new VulkanColorBlendStateBuilder()
                    .AddAttachment(colorBlendAttachments))
                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder()
                     .Add(Vertex.GetBindingDescription(), Vertex.GetAttributeDescriptions()))
                .AddRenderPass<DeferredPassStrategy>(IoC.Container.GetInstance<RenderPassManager>())
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

            _renderer.PipelineManager.Create(pipelineBuilder);
        }

        public void OnDetach()
        {
        }

        public void OnImGuiRender(VkCommandBuffer vkCommandBuffer)
        {
            _dockSpace.Begin();
            _mainMenuBar.Draw();
            _toolbar.Draw();

            // Draw all windows
            _sceneHierarchy.Draw();
            _inspector.Draw();
            _sceneViewport.Draw();
            _gameViewport.Draw();
            _console.Draw();
            _performance.Draw();

            _dockSpace.End();
        }
       
        public void OnRender(VkCommandBuffer vkCommandBuffer)
        {
        }

        public void OnUpdate()
        {
            _sceneViewport.Update();
            _gameViewport.Update();
        }
    }
}
