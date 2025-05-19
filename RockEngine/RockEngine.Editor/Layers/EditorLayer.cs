using ImGuiNET;

using RockEngine.Core;
using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Core.Rendering.ImGuiRendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Editor.EditorComponents;
using RockEngine.Editor.UIAttributes;
using RockEngine.Vulkan;
using RockEngine.Vulkan.Builders;

using Silk.NET.Input;
using Silk.NET.Vulkan;

using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace RockEngine.Editor.Layers
{
    public class EditorLayer : ILayer
    {
        private readonly World _world;
        private readonly VulkanContext _context;
        private readonly GraphicsEngine _graphicsEngine;
        private readonly Renderer _renderer;
        private readonly IInputContext _inputContext;
        private readonly TextureStreamer _textureStreamer;
        private readonly ImGuiController _imGuiController;
        private VkPipelineLayout _pipelineLayout;
        private VkPipeline _pipeline;
        private IBLParams _iblParams = new IBLParams();


        private readonly List<Vector3> _lightCenters = new List<Vector3>();
        private readonly List<float> _lightSpeeds = new List<float>();
        private readonly List<float> _lightRadii = new List<float>();
        private Entity _selectedEntity;

        public EditorLayer(World world, VulkanContext context, GraphicsEngine graphicsEngine, Renderer renderer, IInputContext inputContext, TextureStreamer textureStreamer)
        {
            _world = world;
            _context = context;
            _graphicsEngine = graphicsEngine;
            _renderer = renderer;
            _inputContext = inputContext;
            _textureStreamer = textureStreamer;
            _imGuiController = new ImGuiController(context, graphicsEngine, renderer.BindingManager, _inputContext, graphicsEngine.Swapchain.Extent.Width, graphicsEngine.Swapchain.Extent.Height, renderer.SwapchainTarget);
        }

        public async Task OnAttach()
        {
            await CretePipeline();
            await CreateSolidPipeline();
            using var pool = VkCommandPool.Create(_context, new CommandPoolCreateInfo
            {
                SType = StructureType.CommandPoolCreateInfo,
                Flags = CommandPoolCreateFlags.TransientBit | CommandPoolCreateFlags.ResetCommandBufferBit
            });
            using AssimpLoader assimpLoader = new AssimpLoader(_textureStreamer);
            var revoulierTask = assimpLoader.LoadMeshesAsync("Resources\\Models\\Revoulier\\Cerberus_LP.FBX", _context);
            var sponza = await assimpLoader.LoadMeshesAsync("Resources\\Models\\SponzaAtrium\\scene.gltf", _context);
            var cubemap = await Texture.CreateCubeMapAsync(_context, [
                "Resources/skybox/right.jpg",    // +X
                "Resources/skybox/left.jpg",     // -X
                "Resources/skybox/top.jpg",      // +Y (Vulkan's Y points down)
                "Resources/skybox/bottom.jpg",   // -Y
                "Resources/skybox/front.jpg",    // +Z
                "Resources/skybox/back.jpg"      // -Z
            ]);
            var skybox = _world.CreateEntity();
             skybox.AddComponent<Skybox>().Cubemap = cubemap;
            skybox.Transform.Scale = new Vector3(100,100,100);

            var cam = _world.CreateEntity();
            var debugCam = cam.AddComponent<DebugCamera>();
            debugCam.SetInputContext(_inputContext);

            foreach (var item in sponza)
            {
                var entity = _world.CreateEntity();
                entity.Transform.Scale = new Vector3(0.1f);
                entity.Transform.Position = new Vector3(0);
                var mesh = entity.AddComponent<Mesh>();
                mesh.SetMeshData(item.Vertices, item.Indices);
                mesh.Material = new Material(_pipeline, item.Textures);
            }

            var revoulier = await revoulierTask;
            foreach (var item in revoulier)
            {
                var entity = _world.CreateEntity();
                entity.Transform.Scale = new Vector3(0.1f);
                entity.Transform.Position = new Vector3(0,10,0);
                entity.Transform.Rotation = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateRotationZ(90));
                var mesh = entity.AddComponent<Mesh>();
                mesh.SetMeshData(item.Vertices, item.Indices);
                mesh.Material = new Material(_pipeline, item.Textures);
                entity.Name = "Revoulier";
            }

            for (int i = 0; i < 100; i++)
            {
                var lightEntity = _world.CreateEntity();
                var light = lightEntity.AddComponent<Light>();
                var transform = lightEntity.Transform;
                var mesh = lightEntity.AddComponent<Mesh>();
                mesh.SetMeshData(DefaultMeshes.Cube.Vertices, DefaultMeshes.Cube.Indices);
                mesh.Material = new Material(_renderer.PipelineManager.GetPipelineByName("Solid"));
                lightEntity.Layer = RenderLayerType.Solid;
                // Random position within 200X200X200 cube centered at origin
                transform.Position = new Vector3(
                    (Random.Shared.NextSingle() * 200) - 100,
                    (Random.Shared.NextSingle() * 200) - 100,
                    (Random.Shared.NextSingle() * 200) - 100
                );
                light.Intensity = 20;

                // Random light type distribution
                float typeRand = Random.Shared.NextSingle();
                if (typeRand < 0.7f) // 70% Point lights
                {
                    light.Type = LightType.Point;
                    light.Color = new Vector3(
                        Random.Shared.NextSingle(),
                        Random.Shared.NextSingle(),
                        Random.Shared.NextSingle()
                    );
                    light.Radius = Random.Shared.NextSingle() * 1000 + 5;
                    // Store movement parameters
                    _lightCenters.Add(transform.Position);
                    _lightSpeeds.Add(Random.Shared.NextSingle() * 2 + 0.5f);
                    _lightRadii.Add(Random.Shared.NextSingle() * 5 + 2);
                }
                else if (typeRand < 0.9f) // 20% Spot lights
                {
                    light.Type = LightType.Spot;
                    light.Color = new Vector3(
                        Random.Shared.NextSingle(),
                        Random.Shared.NextSingle(),
                        Random.Shared.NextSingle()
                    );
                    light.Radius = Random.Shared.NextSingle() * 1000 + 5;
                    light.InnerCutoff = 0.85f;
                    light.OuterCutoff = 0.6f;

                    // Store movement parameters
                    _lightCenters.Add(transform.Position);
                    _lightSpeeds.Add(Random.Shared.NextSingle() * 1.5f + 0.5f);
                    _lightRadii.Add(Random.Shared.NextSingle() * 3 + 1);
                    cam.AddChild(lightEntity);

                }
               /* else // 10% Directional lights
                {
                    light.Type = LightType.Directional;
                    light.Color = new Vector3(1, 1, 0.8f);
                    light.Intensity = 0.3f;
                    light.Direction = Vector3.Normalize(new Vector3(
                        Random.Shared.NextSingle() - 0.5f,
                        -1.0f,
                        Random.Shared.NextSingle() - 0.5f
                    ));
                }*/

               mesh.Material.PushConstant("color", light.Color);
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

        private async Task CretePipeline()
        {
            VkShaderModule vkShaderModuleFrag =
                await VkShaderModule.CreateAsync(_context, "Shaders\\Geometry.frag.spv", ShaderStageFlags.FragmentBit);

            VkShaderModule vkShaderModuleVert =
                await VkShaderModule.CreateAsync(_context, "Shaders\\Geometry.vert.spv", ShaderStageFlags.VertexBit);

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
        private async Task CreateSolidPipeline()
        {
            var vertShader = await VkShaderModule.CreateAsync(_context, "Shaders/Solid.vert.spv", ShaderStageFlags.VertexBit);
            var fragShader = await VkShaderModule.CreateAsync(_context, "Shaders/Solid.frag.spv", ShaderStageFlags.FragmentBit);
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
            ImGui.DockSpaceOverViewport(1,ImGui.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode);
            ImGui.ShowDemoWindow();
            DrawFps();
            DrawAllocationStats();
            DrawPerformanceMetrics();
            if (ImGui.Begin("RENDER"))
            {
                var debugCam = _world.GetEntities().First(s=>s.GetComponent<DebugCamera>() is not null).GetComponent<DebugCamera>();
                var renderTarget = debugCam.RenderTarget;
                var texId = _imGuiController.GetTextureID(renderTarget.OutputTexture);
                // Get proper size maintaining aspect ratio
                var imageSize = new Vector2(renderTarget.OutputTexture.Image.Extent.Width, renderTarget.OutputTexture.Image.Extent.Height);
                var availableSize = ImGui.GetContentRegionAvail();
                var scale = Math.Min(availableSize.X / imageSize.X, availableSize.Y / imageSize.Y);
                var displaySize = imageSize * scale;

                ImGui.Image(texId, displaySize);
                ImGui.End();
            }

            var cameras = _world.GetEntities().Where(s=>s.GetComponent<Camera>() is not null);
            foreach (var camEntity in cameras)
            {
                var cam = camEntity.GetComponent<Camera>();
                if (ImGui.Begin("Camera"))
                {
                    ImGui.DragFloat("IblParams.exposure", ref _iblParams.exposure, 0.1f,0.1f, 4.0f);
                    ImGui.DragFloat("IblParams.envIntensity", ref _iblParams.envIntensity, 0.0f, 0.1f, 2.0f);
                    ImGui.DragFloat("IblParams.aoStrength", ref _iblParams.aoStrength, 0.0f, 0.1f, 2.0f);
                    cam.RenderTarget.GBuffer.Material.PushConstant("iblParams",_iblParams);
                    ImGui.End();
                }
            }

            // Existing windows and new UI components
            DrawSceneHierarchy();
            DrawInspector();
        }
        public void OnRender(VkCommandBuffer vkCommandBuffer)
        {
            _renderer.AddCommand(new ImguiRenderCommand(_imGuiController.Render));
        }

        public void OnUpdate()
        {
            _imGuiController.Update();
            float time = (float)Time.TotalTime;
            var lightEntities = _world.GetEntities().Where(s => s.GetComponent<Light>() != null).ToArray();
            var camEntity = _world.GetEntities().FirstOrDefault(s => s.GetComponent<Camera>() != null);

            for (int i = 0; i < lightEntities.Length; i++)
            {
                Entity entity = lightEntities[i];
                var light = entity.GetComponent<Light>();
                var transform = entity.Transform;

                switch (light.Type)
                {
                    case LightType.Point when i < _lightCenters.Count:
                        // Orbital movement around initial position
                        Vector3 center = _lightCenters[i];
                        float speed = _lightSpeeds[i];
                        float radius = _lightRadii[i];

                        transform.Position = center + new Vector3(
                            MathF.Sin(time * speed) * radius,
                            MathF.Sin(time * speed * 0.8f) * radius * 0.5f,
                            MathF.Cos(time * speed) * radius
                        );
                        break;

                    case LightType.Spot when i < _lightCenters.Count:
                        // Vertical circular movement
                       /* if (i % 10 == 0) // Every 10th spot light follows camera
                        {
                            if (camEntity != null)
                            {
                                //transform.Position = camEntity.Transform.Position;
                                light.Direction = camEntity.GetComponent<Camera>().Front;
                            }
                        }
                        else
                        {*/
                            center = _lightCenters[i];
                            speed = _lightSpeeds[i];
                            radius = _lightRadii[i];

                            transform.Position = center + new Vector3(
                                MathF.Cos(time * speed) * radius,
                                MathF.Sin(time * speed * 2) * radius,
                                MathF.Sin(time * speed) * radius
                            );
                            light.Direction = Vector3.Normalize(center - transform.Position);
                        //}
                        break;

                    case LightType.Directional:
                        // Slow directional rotation
                        Quaternion rot = Quaternion.CreateFromAxisAngle(
                            Vector3.Normalize(light.Direction),
                            (float)Time.DeltaTime * 0.3f
                        );
                        light.Direction = Vector3.Transform(light.Direction, rot);
                        break;
                }
            }
        }

        private void DrawFps()
        {
            if (ImGui.Begin("FPS"))
            {
                ImGui.Text(Time.FPS.ToString());
                ImGui.End();
            }
        }
        private void DrawPerformanceMetrics()
        {
            PerformanceTracer.DrawMetrics();
        }

        private void DrawAllocationStats()
        {

            if (ImGui.Begin("Memory Allocation Diagram"))
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
                ImGui.End();
            }

        }
        private void DrawSceneHierarchy()
        {
            ImGui.Begin("Scene Hierarchy");
            foreach (var entity in _world.GetEntities().Where(e => e.Parent == null))
            {
                DrawEntityNode(entity);
            }
            ImGui.End();
        }
        private void DrawInspector()
        {
            if (_selectedEntity == null) return;

            ImGui.Begin("Inspector");
            foreach (var component in _selectedEntity.Components)
            {
                if (ImGui.CollapsingHeader(component.GetType().Name))
                    DrawComponentUI(component);
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

            bool isOpen = ImGui.TreeNodeEx(entity.Name, flags);
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
                //if (uiAttr == null) continue;

                string label = !string.IsNullOrEmpty(uiAttr?.DisplayName) ? uiAttr.DisplayName : property.Name;
                if (!property.CanWrite)
                {
                    ImGui.BeginDisabled();
                }

                if (property.PropertyType == typeof(float))
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
                else if (property.PropertyType == typeof(Material))
                {
                    HandleMaterialProperty(component, property, label);
                }
                else if (property.PropertyType == typeof(Texture))
                {
                    HandleTextureProperty(component, property, label);
                }
                // Add more type handlers as needed

                if (!property.CanWrite)
                {
                    ImGui.EndDisabled();
                }
            }
        }

        private void HandleTexturePreview(Texture texture)
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
            var texture = (Texture)property.GetValue(component);
            if (texture == null)
            {
                ImGui.Text($"{label}: None");
                return;
            }

            if (ImGui.TreeNodeEx(label, ImGuiTreeNodeFlags.DefaultOpen))
            {
                HandleTexturePreview(texture);
                ImGui.TreePop();
            }
        }

        private void HandleMaterialProperty(IComponent component, PropertyInfo property, string label)
        {
            var material = (Material)property.GetValue(component);
            if (material == null) return;

            if (ImGui.TreeNodeEx(label, ImGuiTreeNodeFlags.DefaultOpen))
            {
                // Display pipeline information
                ImGui.Text($"Pipeline: {material.Pipeline.Name}");

                // Texture bindings section
                if (ImGui.CollapsingHeader("Texture Bindings", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    for (int i = 0; i < material.Textures.Length; i++)
                    {
                        var texture = material.Textures[i];
                        ImGui.PushID(i);

                        if (texture != null)
                        {
                            if (ImGui.TreeNodeEx($"Binding {i}", ImGuiTreeNodeFlags.Leaf))
                            {
                                // Display texture preview and info
                                HandleTexturePreview(texture);
                                ImGui.TreePop();
                            }
                        }
                        else
                        {
                            ImGui.TextDisabled($"Binding {i}: Empty");
                        }

                        ImGui.PopID();
                    }
                }

                // Push constants section
                if (material.PushConstants.Count > 0 && ImGui.CollapsingHeader("Material Properties"))
                {
                    foreach (var constant in material.PushConstants.Values)
                    {
                        // Example implementation for Vector4 push constants
                        if (constant.Size == Unsafe.SizeOf<Vector4>())
                        {
                            Vector4 value = default;
                            unsafe
                            {
                                fixed (byte* ptr = constant.Value)
                                {
                                    value = *(Vector4*)ptr;
                                }
                            }

                            if (ImGui.ColorEdit4(constant.Name, ref value))
                            {
                                unsafe
                                {
                                    fixed (byte* ptr = constant.Value)
                                    {
                                        *(Vector4*)ptr = value;
                                    }
                                }
                            }
                        }
                        // Add other type handlers as needed
                    }
                }

                ImGui.TreePop();
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
           
            if (isSetted )
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
            if(ImGui.Checkbox(label, ref value))
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
