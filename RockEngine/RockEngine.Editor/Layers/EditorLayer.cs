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
using RockEngine.Core.Rendering.Passes;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Editor.EditorUI;
using RockEngine.Editor.EditorUI.EditorWindows;
using RockEngine.Editor.EditorUI.ImGuiRendering;
using RockEngine.Editor.EditorUI.Logging;
using RockEngine.Editor.Selection;
using RockEngine.Vulkan;

using Silk.NET.Input;
using Silk.NET.Vulkan;

using System.Numerics;

namespace RockEngine.Editor.Layers
{
    public class EditorLayer : ILayer
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly World _world;
        private readonly VulkanContext _context;
        private readonly GraphicsContext _graphicsEngine;
        private readonly WorldRenderer _renderer;
        private readonly IInputContext _inputContext;
        private readonly AssetManager _assetManager;
        private readonly EditorConsole _editorConsole;
        private readonly ISelectionManager _selectionManager;

        // UI Components
        private readonly EditorDockSpace _dockSpace;
        private readonly MainMenuBar _mainMenuBar;
        private readonly Toolbar _toolbar;
        private readonly SceneHierarchyWindow _sceneHierarchy;
        private readonly InspectorWindow _inspector;
        private readonly ViewportWindow _sceneViewport;
        private readonly ViewportWindow _gameViewport;
        private readonly ConsoleWindow _console;
        private readonly PerformanceWindow _performance;

        public EditorLayer(
            World world,
            VulkanContext context,
            GraphicsContext graphicsEngine,
            WorldRenderer renderer,
            IInputContext inputContext,
            AssetManager assetManager,
            EditorConsole editorConsole,
            ImGuiController imGuiController,
            ISelectionManager selectionManager)
        {
            _world = world;
            _context = context;
            _graphicsEngine = graphicsEngine;
            _renderer = renderer;
            _inputContext = inputContext;
            _assetManager = assetManager;
            _editorConsole = editorConsole;
            _selectionManager = selectionManager;

            // Initialize UI components
            _dockSpace = new EditorDockSpace();
            _mainMenuBar = new MainMenuBar();
            _toolbar = new Toolbar();
            _sceneHierarchy = new SceneHierarchyWindow(world, _selectionManager);
            _inspector = new InspectorWindow(assetManager, imGuiController, _selectionManager);
            _sceneViewport = new ViewportWindow("Scene View", world, inputContext, imGuiController, _selectionManager);
            _gameViewport = new ViewportWindow("Game View", world, inputContext, imGuiController);
            _console = new ConsoleWindow(editorConsole);
            _performance = new PerformanceWindow();

            // Connect event handlers
            _mainMenuBar.ViewToggled += OnViewToggled;

            // Apply theme and initialize
            EditorTheme.ApplyModernDarkTheme();
          
        }

        private void OnViewToggled(string viewName, bool isVisible)
        {
            switch (viewName)
            {
                case "Scene Hierarchy": _sceneHierarchy.IsOpen = isVisible; break;
                case "Inspector": _inspector.IsOpen = isVisible; break;
                case "Console": _console.IsOpen = isVisible; break;
                case "Performance": _performance.IsOpen = isVisible; break;
                case "Material Templates": /* TODO */ break;
                case "Memory Stats": /* TODO */ break;
            }
        }

        public async Task OnAttach()
        {
            CreateSolidPipeline();
            // Uncomment to load assets when needed
            await LoadOrCreateAssets();

        }

        private async Task LoadOrCreateAssets()
        {
            bool loadFromProject = false;

          /*  if (loadFromProject)
            {
                await LoadAssetsFromProject(
                    @"X:\RockEngine.Vulkan\RockEngine\RockEngine.Editor\bin\Debug\net9.0\Project\TestAssetSystem\DebugProject\DebugProject.rockproj");
            }
            else
            {
                await CreateAssetsProgrammatically();
            }*/
        }

        private async Task LoadAssetsFromProject(string projectPath)
        {
            try
            {
                _logger.Info($"Loading assets from project: {projectPath}");
                await _assetManager.LoadAssetAsync<ProjectAsset>(projectPath);

                var scene = await _assetManager.LoadAsync<SceneData>(new AssetPath("Scenes", "DebugScene"));
                await scene.LoadDataAsync();
                await ((SceneAsset)scene).InstantiateEntities();

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

                var project = await _assetManager.CreateProjectAsync("X:\\RockEngine.Vulkan\\RockEngine\\RockEngine.Editor\\TestProject", "DebugProject");

                var scene = _assetManager.Create<SceneAsset>(new AssetPath("Scenes", "DebugScene"));
                scene.SetData(new SceneData());

                var skyboxAsset = await CreateTexture("Skybox", "debug_skybox", [
                    "Resources/skybox/right.jpg", "Resources/skybox/left.jpg",
                    "Resources/skybox/top.jpg", "Resources/skybox/bottom.jpg",
                    "Resources/skybox/front.jpg", "Resources/skybox/back.jpg"
                ], TextureType.TextureCube);

                var cubeMesh = _assetManager.GetAsset<MeshAsset>(DefaultMeshes.CubeAssetID);
                await CreateSceneEntities(scene, cubeMesh, skyboxAsset).ConfigureAwait(false);

                var sponza = await _assetManager.LoadModelAsync("Resources\\Models\\SponzaAtrium\\scene.gltf", "Sponza");
                CreateModelEntities(scene, sponza, new Vector3(0), new Vector3(0.1f));

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

            // Create lights with solid materials
            for (int i = 0; i < 100; i++)
            {
                var lightColor = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());

                var solidMaterial = _assetManager.Create<MaterialAsset>(new AssetPath("Materials", $"solid{i}"));
                solidMaterial.SetData(new MaterialData
                {
                    PipelineName = "Solid",
                    Parameters = new Dictionary<string, object> { ["color"] = lightColor }
                });

                var light = scene.CreateEntity();
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
                lightMeshRenderer.SetProviders(cubeMesh, solidMaterial);
                lightComponent.Intensity = 100;
                lightComponent.Radius = 100;
                lightComponent.Color = lightColor;

                await _assetManager.SaveAsync(solidMaterial);
            }
        }

        private void CreateModelEntities(SceneAsset scene, ModelAsset model, Vector3 position, Vector3 scale, Quaternion? rotation = null)
        {
            var parent = scene.CreateEntity();
            parent.Name = model.Name;
            parent.Transform.Position = position;
            parent.Transform.Scale = scale;
            if (rotation.HasValue)
            {
                parent.Transform.Rotation = rotation.Value;
            }

            foreach (var part in model.Parts)
            {
                var entity = scene.CreateEntity();
                entity.AddComponent<MeshRenderer>()
                    .SetProviders(part.Mesh, part.Material);

                parent.AddChild(entity);
            }
        }

        private async Task SaveAssetsAsync(params IAsset[] assets)
        {
            foreach (var asset in assets)
            {
                await _assetManager.SaveAsync(asset);
            }
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
                    ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                   ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                    BlendEnable = false
                }
            };

            using var pipelineBuilder = GraphicsPipelineBuilder.CreateDefault<DeferredPassStrategy>(
                _context, "Solid", IoC.Container, [vertShader, fragShader]);

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

            _renderer.PipelineManager.Create(pipelineBuilder);
        }

        public void OnDetach() { }

        public async ValueTask OnImGuiRender(VkCommandBuffer vkCommandBuffer)
        {
            _dockSpace.Begin();
            _mainMenuBar.Draw();
            _toolbar.Draw();

            // Draw all windows
            await _sceneHierarchy.Draw();
            await _inspector.Draw();
            await _sceneViewport.Draw();
            await _gameViewport.Draw();
            await _console.Draw();
            await _performance.Draw();

            _dockSpace.End();
        }

        public void OnRender(VkCommandBuffer vkCommandBuffer) { }

        public void OnUpdate()
        {
            _sceneViewport.Update();
            _gameViewport.Update();
        }
    }
}