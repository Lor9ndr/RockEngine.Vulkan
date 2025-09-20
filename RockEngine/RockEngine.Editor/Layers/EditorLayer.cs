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
using RockEngine.Core.Rendering.Commands;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Editor.EditorComponents;
using RockEngine.Editor.EditorUI.ImGuiRendering;
using RockEngine.Editor.EditorUI.Logging;
using RockEngine.Editor.UIAttributes;
using RockEngine.Vulkan;

using Silk.NET.Input;
using Silk.NET.Vulkan;

using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
        private readonly ImGuiController _imGuiController;
        private VkPipelineLayout _pipelineLayout;
        private VkPipeline _pipeline;
        private IBLParams _iblParams = new IBLParams();

        private const string ICON_PLAY = "\uf04b";       // fa-play
        private const string ICON_PAUSE = "\uf04c";      // fa-pause
        private const string ICON_STOP = "\uf04d";       // fa-stop
        private const string ICON_STEP = "\uf051";       // fa-step-forward
        private const string ICON_SETTINGS = "\uf013";   // fa-cog

        private readonly List<Vector3> _lightCenters = new List<Vector3>();
        private readonly List<float> _lightSpeeds = new List<float>();
        private readonly List<float> _lightRadii = new List<float>();
        private Entity _selectedEntity;
        private Vector2 _currentContentSize;
        private Vector2 _currentGameSize;
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
            _imGuiController = imGuiController;
            _stateManager = new EditorStateManager();


            DefaultMeshes.Initalize(_assetManager);
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

                // Create a new project
                var project = await _assetManager.CreateProjectAsync("DebugProject", "X:\\RockEngine.Vulkan\\RockEngine\\RockEngine.Editor\\bin\\Debug\\net9.0\\Project\\TestAssetSystem");

                // Create a scene
                var scene = _assetManager.Create<SceneAsset>(new AssetPath("Scenes", "DebugScene"));
                scene.SetData(new SceneData());

                // Create a skybox texture
                var skyboxAsset = _assetManager.Create<TextureAsset>(new AssetPath("Skybox", "debug_skybox"));
                skyboxAsset.SetData(new TextureData()
                {
                    FilePaths = [
                    "Resources/skybox/right.jpg",
                    "Resources/skybox/left.jpg",
                    "Resources/skybox/top.jpg",
                    "Resources/skybox/bottom.jpg",
                    "Resources/skybox/front.jpg",
                    "Resources/skybox/back.jpg"
                    ],
                    GenerateMipmaps = false,
                    Type = TextureType.TextureCube
                });

                // Create a default material
                var geomMaterial = _assetManager.Create<MaterialAsset>(new AssetPath("Materials", "geometry"));
                geomMaterial.SetData(new MaterialData()
                {
                    PipelineName = "Geometry",
                    TextureAssetIDs = []
                });

               

                // Create a cube mesh
                var cubeMesh = _assetManager.GetAsset<MeshAsset>(DefaultMeshes.CubeAssetID);

                // Create entities
                var skybox = scene.CreateEntity();
                skybox.AddComponent<Skybox>().Cubemap = skyboxAsset;
                skybox.Transform.Scale = new Vector3(100, 100, 100);

                var camera = scene.CreateEntity();
                camera.AddComponent<Camera>();
                camera.Name = "Camera";
                for (int i = 0; i < 100; i++)
                {
                    var solidMaterial = _assetManager.Create<MaterialAsset>(new AssetPath("Materials", $"solid{i}"));
                    solidMaterial.SetData(new MaterialData()
                    {
                        PipelineName = "Solid",
                        TextureAssetIDs = []
                    });
                    var light = scene.CreateEntity();
                    light.Layer = RenderLayerType.Solid;
                    light.Name = $"light ({i})";
                    light.Transform.Scale *= 0.5f;
                    var lightComponent = light.AddComponent<Light>();
                    lightComponent.Type = LightType.Point;
                    light.Transform.Position = new Vector3(Random.Shared.NextSingle()*100, 20 + Random.Shared.NextSingle()*100, Random.Shared.NextSingle()*100);
                    var lightMeshRenderer = light.AddComponent<MeshRenderer>();
                    lightMeshRenderer.SetAssets(cubeMesh, solidMaterial);
                    lightComponent.Intensity = 100;
                    lightComponent.Radius = 100;
                    lightComponent.Color = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());
                    await _assetManager.SaveAsync(solidMaterial);

                }

                var cube = scene.CreateEntity();
                cube.Name = "CUBE";
                var meshRenderer = cube.AddComponent<MeshRenderer>();
                meshRenderer.SetAssets(cubeMesh, geomMaterial);
                cube.Transform.Position = new Vector3(0, 0, 5);

                var sponza = await _assetManager.LoadModelAsync("Resources\\Models\\SponzaAtrium\\scene.gltf", "Sponza");
                CreateModelEntities(scene, sponza, new Vector3(0), new Vector3(0.1f));

                // Save all assets
                await _assetManager.SaveAsync(sponza);
                await _assetManager.SaveAsync(skyboxAsset);
                await _assetManager.SaveAsync(geomMaterial);
                await _assetManager.SaveAsync(scene);


                // Instantiate the scene
                //scene.InstantiateEntities();

                _logger.Info("Programmatic assets created successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to create assets programmatically");
                throw;
            }
        }

        // Modify the OnAttach method to use these methods
        public async Task OnAttach()
        {
            CretePipeline();
            CreateSolidPipeline();

            // Choose between loading from project or creating programmatically
           /* bool loadFromProject = false;

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
        private void CreateModelEntities(SceneAsset scene,ModelAsset model, Vector3 position,
                               Vector3 scale, Quaternion? rotation = null)
        {
            var parent = scene.CreateEntity();
            parent.Name = model.Name;
            parent.Transform.Position = position;
            parent.Transform.Scale = scale;
            if (rotation.HasValue) parent.Transform.Rotation = rotation.Value;

            foreach (var part in model.Parts)
            {
                var entity = scene.CreateEntity();
                var renderer = entity.AddComponent<MeshRenderer>();
                renderer.SetAssets(part.Mesh, part.Material);
                parent.AddChild(entity);
            }
        }

        private struct IBLParams
        {
            public float exposure = 0.1f;
            public float envIntensity = 1.0f;
            public float aoStrength = 1.0f;

            public IBLParams()
            {
                this.exposure = 0.1f;
                this.envIntensity = 1.0f;
                this.aoStrength = 1.0f;
            }
        }

        private void CretePipeline()
        {
            var shaderManager = IoC.Container.GetInstance<IShaderManager>();
            VkShaderModule vkShaderModuleFrag = VkShaderModule.Create(_context,shaderManager.GetShader("Geometry.frag"), ShaderStageFlags.FragmentBit);

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
                 .AddRenderPass(_renderer.RenderPass.RenderPass)
                 .WithPipelineLayout(_pipelineLayout)
                 .WithSubpass(0)
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
            var vertShader =  VkShaderModule.Create(_context, shaderManager.GetShader("Solid.vert"), ShaderStageFlags.VertexBit);
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
                .AddRenderPass(_renderer.RenderPass.RenderPass)
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

            _renderer.PipelineManager.Create(pipelineBuilder);
        }

        public void OnDetach()
        {
        }

        public void OnImGuiRender(VkCommandBuffer vkCommandBuffer)
        {
            ImGui.ShowDemoWindow();
           // DrawToolbar();
            DrawFps();
            DrawAllocationStats();
            DrawPerformanceMetrics();

            if (ImGui.Begin("Scene##EditorScreen"))
            {
                var debugCam = _world.GetEntities()
                    .FirstOrDefault(s => s.GetComponent<DebugCamera>() is not null)?
                    .GetComponent<DebugCamera>();
                if (debugCam != null)
                {
                    var windowHovered = ImGui.IsWindowHovered();
                    if (windowHovered)
                    {
                        _inputContext.Mice[0].Cursor.StandardCursor = StandardCursor.Hand;
                    }
                    else
                    {
                        _inputContext.Mice[0].Cursor.StandardCursor = StandardCursor.Arrow;
                    }

                    if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                    {
                        debugCam.CanMove = windowHovered;
                    }
                    else
                    {
                        debugCam.CanMove = false;
                    }


                    var renderTarget = debugCam.RenderTarget;
                    if (renderTarget != null)
                    {
                        var texId = _imGuiController.GetTextureID(renderTarget.OutputTexture);
                        // Get proper size maintaining aspect ratio
                        var imageSize = new Vector2(renderTarget.OutputTexture.Width, renderTarget.OutputTexture.Height);
                        var availableSize = ImGui.GetContentRegionAvail();
                        var scale = Math.Min(availableSize.X / imageSize.X, availableSize.Y / imageSize.Y);
                        var displaySize = imageSize * scale;

                        ImGui.Image(texId, displaySize);
                        _currentContentSize = availableSize;
                    }
                }
            }
            ImGui.End();

            if (ImGui.Begin("Game##GameScreen"))
            {
                var camera = _world.GetEntities()
                    .FirstOrDefault(s => s.GetComponent<Camera>() is not null && s.GetComponent<DebugCamera>() is null)?
                    .GetComponent<Camera>();
                if (camera != null)
                {
                    var renderTarget = camera.RenderTarget;
                    if (renderTarget != null)
                    {
                        var texId = _imGuiController.GetTextureID(renderTarget.OutputTexture);
                        // Get proper size maintaining aspect ratio
                        var imageSize = new Vector2(renderTarget.OutputTexture.Width, renderTarget.OutputTexture.Height);
                        var availableSize = ImGui.GetContentRegionAvail();
                        var scale = Math.Min(availableSize.X / imageSize.X, availableSize.Y / imageSize.Y);
                        var displaySize = imageSize * scale;

                        ImGui.Image(texId, displaySize);
                        _currentGameSize = availableSize;
                    }
                }

            }
            ImGui.End();


            var cameras = _world.GetEntities().Where(s => s.GetComponent<Camera>() is not null);
            int i = 0;
            foreach (var camEntity in cameras)
            {
                var cam = camEntity.GetComponent<Camera>();
                if (cam != null && ImGui.Begin($"Camera##camera_{i}"))
                {
                    ImGui.DragFloat("IblParams.exposure", ref _iblParams.exposure, 0.1f, 0.1f, 4.0f);
                    ImGui.DragFloat("IblParams.envIntensity", ref _iblParams.envIntensity, 0.0f, 0.1f, 2.0f);
                    ImGui.DragFloat("IblParams.aoStrength", ref _iblParams.aoStrength, 0.0f, 0.1f, 2.0f);
                    cam.RenderTarget?.GBuffer.Material.PushConstant("iblParams", _iblParams);
                }
                ImGui.End();
                i++;
            }

            // Existing windows and new UI components
            DrawSceneHierarchy();
            DrawInspector();
            _editorConsole.Draw();
        }

        private void DrawToolbar()
        {
            float padding = 4f;
            float buttonSize = 32f;
            float iconSize = buttonSize * 0.6f;
            float spacing = 6f;

            // Calculate centered position
            float totalWidth = (buttonSize * 6) + (spacing * 5);
            float startX = (ImGui.GetContentRegionAvail().X - totalWidth) * 0.5f;

            // Style setup
            var style = ImGui.GetStyle();
            var colors = style.Colors;
            Vector4 activeColor = new Vector4(0.26f, 0.59f, 0.98f, 1.00f);
            Vector4 hoverColor = new Vector4(0.26f, 0.59f, 0.98f, 0.4f);
            Vector4 inactiveColor = new Vector4(0.3f, 0.3f, 0.3f, 1.0f);
            Vector4 disabledColor = new Vector4(0.3f, 0.3f, 0.3f, 0.4f);

            // Create toolbar background
            ImGui.BeginChild("##toolbar", new Vector2(0, buttonSize + padding * 2), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

            // Draw subtle background
            var drawList = ImGui.GetWindowDrawList();
            var min = ImGui.GetWindowPos();
            var max = new Vector2(min.X + ImGui.GetWindowWidth(), min.Y + ImGui.GetWindowHeight());
            drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.15f, 0.15f, 0.9f)), style.ChildRounding);

            // Center buttons horizontally
            ImGui.SetCursorPosX(startX);
            ImGui.SetCursorPosY(padding);

            // Button group
            ImGui.BeginGroup();

            // Play Button
            bool isPlayMode = _stateManager.State == EditorState.Play;
            ImGui.PushStyleColor(ImGuiCol.Button, isPlayMode ? activeColor : inactiveColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverColor);
            ImGui.PushStyleColor(ImGuiCol.Text, isPlayMode ? new Vector4(1, 1, 1, 1) : disabledColor);
            if (ImGui.Button($"{ICON_PLAY}##Play", new Vector2(buttonSize, buttonSize)))
            {
                _stateManager.SetState(EditorState.Play);
            }
            ImGui.PopStyleColor(3);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Play Mode");

            ImGui.SameLine(0, spacing);

            // Pause Button
            bool isPaused = _stateManager.State == EditorState.Paused;
            ImGui.BeginDisabled(_stateManager.State != EditorState.Play && !isPaused);
            ImGui.PushStyleColor(ImGuiCol.Button, isPaused ? activeColor : inactiveColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverColor);
            if (ImGui.Button($"{ICON_PAUSE}##Pause", new Vector2(buttonSize, buttonSize)))
            {
                _stateManager.SetState(isPaused ? EditorState.Play : EditorState.Paused);
            }
            ImGui.PopStyleColor(2);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(isPaused ? "Resume" : "Pause");

            ImGui.SameLine(0, spacing);

            // Stop Button
            ImGui.BeginDisabled(_stateManager.State == EditorState.Edit);
            ImGui.PushStyleColor(ImGuiCol.Button, inactiveColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverColor);
            if (ImGui.Button($"{ICON_STOP}##Stop", new Vector2(buttonSize, buttonSize)))
            {
                _stateManager.SetState(EditorState.Edit);
            }
            ImGui.PopStyleColor(2);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Stop");

            ImGui.SameLine(0, spacing);

            // Step Button (optional)
            ImGui.BeginDisabled(_stateManager.State != EditorState.Paused);
            if (ImGui.Button($"{ICON_STEP}##Step", new Vector2(buttonSize, buttonSize)))
            {
                // Step through one frame
            }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Step Forward");

            ImGui.SameLine(0, spacing);

            // Settings Button
            if (ImGui.Button($"{ICON_SETTINGS}##Settings", new Vector2(buttonSize, buttonSize)))
            {
                // Show editor settings
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Editor Settings");

            ImGui.EndGroup();

            // State indicator with colored badge
            ImGui.SameLine(0, spacing * 2);
            ImGui.SetCursorPosY(padding + (buttonSize - ImGui.GetTextLineHeight()) * 0.5f);

            var stateText = _stateManager.State.ToString();
            var stateColor = _stateManager.State switch
            {
                EditorState.Play => new Vector4(0.0f, 0.8f, 0.0f, 1.0f),   // Green
                EditorState.Paused => new Vector4(0.8f, 0.8f, 0.0f, 1.0f), // Yellow
                _ => new Vector4(0.8f, 0.8f, 0.8f, 1.0f)                   // Light Gray
            };

            ImGui.TextColored(stateColor, stateText);

            ImGui.EndChild();
        }
        public void OnRender(VkCommandBuffer vkCommandBuffer)
        {
        }

        public void OnUpdate()
        {
            var debugCam = _world.GetEntities()
                    .FirstOrDefault(s => s.GetComponent<DebugCamera>() is not null)?
                    .GetComponent<DebugCamera>();
            var cam = _world.GetEntities()
                .FirstOrDefault(s => s.GetComponent<Camera>() is not null && s.GetComponent<DebugCamera>() is null)?
                .GetComponent<Camera>();
            _currentContentSize.X = Math.Max(_currentContentSize.X, 1);
            _currentContentSize.Y = Math.Max(_currentContentSize.Y, 1);
            _currentGameSize.X = Math.Max(_currentGameSize.X, 1);
            _currentGameSize.Y = Math.Max(_currentGameSize.Y, 1);
            debugCam?.RenderTarget?.Resize(new Extent2D((uint)_currentContentSize.X, (uint)_currentContentSize.Y));
            cam?.RenderTarget?.Resize(new Extent2D((uint)_currentGameSize.X, (uint)_currentGameSize.Y));
            float time = (float)Time.TotalTime;
            var lightEntities = _world.GetEntities()
                .Where(s => s.GetComponent<Light>() != null).
                ToArray();

            var camEntity = _world.GetEntities()
                .FirstOrDefault(s => s.GetComponent<Camera>() != null);
            for (int i = 0; i < lightEntities.Length; i++)
            {
                Entity entity = lightEntities[i];
                var light = entity.GetComponent<Light>();
                var transform = entity.Transform;
                entity.GetComponent<MeshRenderer>()?.Material.Asset.MaterialInstance?.PushConstant("color", light.Color);
            }
        }

        private void DrawFps()
        {
            if (ImGui.Begin("FPS##FPS"))
            {
                ImGui.Text(Time.FPS.ToString());
            }
           ImGui.End();
        }
        private void DrawPerformanceMetrics()
        {
            PerformanceTracer.DrawMetrics();
        }

        private void DrawAllocationStats()
        {

            if (ImGui.Begin("Memory Allocation Diagram##MemAllocDiagram"))
            {
                // Get memory stats
                CustomAllocator.GetMemoryStats(out long totalAllocatedBytes, out long peakAllocatedBytes,
                                               out int totalAllocationCount, out int currentActiveAllocations);

                // Draw bar graph for total and peak memory
                ImGui.Text("Memory Usage");
                float barHeight = 20;
                Vector2 barSize = new Vector2(ImGui.GetContentRegionAvail().X, barHeight);

                ImGui.ProgressBar((float)totalAllocatedBytes / peakAllocatedBytes, barSize, $"Current: {FormatBytes(totalAllocatedBytes)}");
                ImGui.ProgressBar(1.0f, barSize, $"Peak: {FormatBytes(peakAllocatedBytes)}");

                // Display allocation counts
                ImGui.Text($"Total Allocations: {totalAllocationCount}");
                ImGui.Text($"Active Allocations: {currentActiveAllocations}");
            }
            ImGui.End();
        }
        private void DrawSceneHierarchy()
        {
            ImGui.Begin("Scene Hierarchy##SceneHierarchy");
            foreach (var entity in _world.GetEntities().Where(e => e.Parent == null))
            {
                DrawEntityNode(entity);
            }
            ImGui.End();
        }
        private void DrawInspector()
        {
            if (_selectedEntity == null) return;

            if (ImGui.Begin("Inspector##ComponentInspector"))
            {
                foreach (var component in _selectedEntity.Components)
                {
                    if (ImGui.CollapsingHeader(component.GetType().Name))
                        DrawComponentUI(component);
                }
            }
           
            ImGui.End();
        }

        private void DrawEntityNode(Entity entity)
        {
            ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanFullWidth;
            if (entity.Children.Count == 0)
                flags |= ImGuiTreeNodeFlags.Leaf;

            bool isSelected = _selectedEntity == entity;
            if (isSelected)
                flags |= ImGuiTreeNodeFlags.Selected;

            bool isOpen = ImGui.TreeNodeEx(entity.Name + $"##{entity.ID}", flags);
            if (ImGui.IsItemClicked())
                _selectedEntity = entity;

            if (isOpen)
            {
                foreach (var child in entity.Children)
                    DrawEntityNode(child);
                ImGui.TreePop();
            }
        }

        private void DrawComponentUI(IComponent component)
        {
            var properties = component.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var property in properties)
            {
                var uiAttr = property.GetCustomAttribute<UIEditableAttribute>();
                string label = !string.IsNullOrEmpty(uiAttr?.DisplayName) ? uiAttr.DisplayName : property.Name;

                if (!property.CanWrite)
                {
                    ImGui.BeginDisabled();
                }

                // Handle different property types with drag and drop support
                if (property.PropertyType == typeof(AssetReference<TextureAsset>))
                {
                    HandleTexturePropertyWithDragDrop(component, property, label);
                }
                else if (property.PropertyType == typeof(AssetReference<MaterialAsset>))
                {
                    HandleMaterialPropertyWithDragDrop(component, property, label);
                }
                else if (property.PropertyType == typeof(AssetReference<MeshAsset>))
                {
                    HandleMeshPropertyWithDragDrop(component, property, label);
                }
                else if (property.PropertyType == typeof(float))
                {
                    HandleFloatProperty(component, property, label);
                }
                else if (property.PropertyType == typeof(Vector3))
                {
                    HandleVector3Property(component, property, label);
                }
                else if (property.PropertyType.IsEnum)
                {
                    HandleEnumProperty(component, property, label);
                }
                else if (property.PropertyType == typeof(bool))
                {
                    HandleBoolProperty(component, property, label);
                }

                if (!property.CanWrite)
                {
                    ImGui.EndDisabled();
                }
            }
        }
        private void HandleTexturePropertyWithDragDrop(IComponent component, PropertyInfo property, string label)
        {
            var textureRef = (AssetReference<TextureAsset>)property.GetValue(component);
            string currentName = textureRef?.Asset?.Name ?? "None";

            ImGui.Button($"{label}: {currentName}", new Vector2(ImGui.GetContentRegionAvail().X, 0));

            if (AssetDragDrop.AcceptAssetDrop(out var assetID))
            {
                var textureAsset = _assetManager.GetAsset<TextureAsset>(assetID);
                if (textureAsset != null)
                {
                    property.SetValue(component, new AssetReference<TextureAsset>(textureAsset));
                }
            }

            // Handle drag source if we have a texture
            if (textureRef?.Asset != null && AssetDragDrop.BeginDragDropSource(textureRef.Asset.ID, textureRef.Asset.Name))
            {
            }

            // Show preview on hover
            if (textureRef?.Asset?.Texture is Texture2D texture2D && ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                HandleTexturePreview(texture2D);
                ImGui.EndTooltip();
            }
        }

        private void HandleTexturePreview(Texture2D texture)
        {
            if (texture == null) return;

            // Get ImGui-compatible texture ID
            IntPtr texId = _imGuiController.GetTextureID(texture);

            // Display texture image with maintain aspect ratio
            float previewWidth = Math.Min(ImGui.GetContentRegionAvail().X, 200);
            Vector2 previewSize = new Vector2(previewWidth, previewWidth * (texture.Height / (float)texture.Width));

            ImGui.Image(texId, previewSize);

            // Display texture metadata
            ImGui.Text($"Resolution: {texture.Width}x{texture.Height}");
            ImGui.Text($"Mip Levels: {texture.LoadedMipLevels}/{texture.TotalMipLevels}");
            if (!string.IsNullOrEmpty(texture.SourcePath))
            {
                ImGui.TextWrapped($"Source: {texture.SourcePath}");
            }
        }

        private void HandleTextureProperty(IComponent component, PropertyInfo property, string label)
        {
            var texture = (AssetReference<TextureAsset>)property.GetValue(component);
            if (texture == null)
            {
                ImGui.Text($"{label}: None");
                return;
            }

            if (ImGui.TreeNodeEx(label, ImGuiTreeNodeFlags.DefaultOpen))
            {
                if(texture.Asset.Texture != null && texture.Asset.Texture is Texture2D texture2D)
                HandleTexturePreview(texture2D);
                ImGui.TreePop();
            }
        }

        private void HandleMaterialPropertyWithDragDrop(IComponent component, PropertyInfo property, string label)
        {
            var materialRef = (AssetReference<MaterialAsset>)property.GetValue(component);
            string currentName = materialRef?.Asset?.Name ?? "None";

            ImGui.Button($"{label}: {currentName}", new Vector2(ImGui.GetContentRegionAvail().X, 0));

            if (AssetDragDrop.AcceptAssetDrop(out var assetID))
            {
                var materialAsset = _assetManager.GetAsset<MaterialAsset>(assetID);
                if (materialAsset != null)
                {
                    property.SetValue(component, new AssetReference<MaterialAsset>(materialAsset));
                }
            }

            if (materialRef?.Asset != null && AssetDragDrop.BeginDragDropSource(materialRef.AssetID, materialRef.Asset.Name))
            {
            }
        }
        private void HandleMeshPropertyWithDragDrop(IComponent component, PropertyInfo property, string label)
        {
            var meshRef = (AssetReference<MeshAsset>)property.GetValue(component);
            string currentName = meshRef?.Asset?.Name ?? "None";

            ImGui.Button($"{label}: {currentName}", new Vector2(ImGui.GetContentRegionAvail().X, 0));

            if (AssetDragDrop.AcceptAssetDrop(out Guid assetId))
            {
                var meshAsset = _assetManager.GetAsset<MeshAsset>(assetId);
                if (meshAsset != null)
                {
                    property.SetValue(component, new AssetReference<MeshAsset>(meshAsset));
                }
            }

            if (meshRef?.Asset != null && AssetDragDrop.BeginDragDropSource(meshRef.AssetID, meshRef.Asset.Name))
            {
            }
        }

        private void HandleFloatProperty(IComponent component, PropertyInfo property, string label)
        {
            float value = (float)property.GetValue(component);
            var range = property.GetCustomAttribute<RangeAttribute>();

            if (range != null)
                ImGui.DragFloat(label, ref value, 0.1f, range.Min, range.Max);
            else
                ImGui.DragFloat(label, ref value);

            property.SetValue(component, value);
        }

        private void HandleVector3Property(IComponent component, PropertyInfo property, string label)
        {
            Vector3 value = (Vector3)property.GetValue(component);
            bool isColor = property.GetCustomAttribute<ColorAttribute>() != null;
            bool isSetted;

            if (isColor)
            {
                isSetted = ImGui.ColorEdit3(label, ref value);
            }
            else
            {
                isSetted = ImGui.DragFloat3(label, ref value);
            }

            if (isSetted)
            {
                property.SetValue(component, value);
            }
        }

        private void HandleEnumProperty(IComponent component, PropertyInfo property, string label)
        {
            Enum value = (Enum)property.GetValue(component);
            if (ImGui.BeginCombo(label, value.ToString()))
            {
                foreach (Enum enumValue in Enum.GetValues(property.PropertyType))
                {
                    bool isSelected = value.Equals(enumValue);
                    if (ImGui.Selectable(enumValue.ToString(), isSelected))
                    {
                        property.SetValue(component, enumValue);
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }
                ImGui.EndCombo();
            }
        }

        private void HandleBoolProperty(IComponent component, PropertyInfo property, string label)
        {
            bool value = (bool)property.GetValue(component);
            if (ImGui.Checkbox(label, ref value))
            {
                property.SetValue(component, value);
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return $"{size:0.##} {suffixes[order]}";
        }

    }
}
