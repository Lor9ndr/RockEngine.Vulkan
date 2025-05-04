using ImGuiNET;

using RockEngine.Core;
using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Core.Rendering.ImGuiRendering;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Editor.EditorComponents;
using RockEngine.Vulkan;
using RockEngine.Vulkan.Builders;

using Silk.NET.Input;
using Silk.NET.Vulkan;

using System.Numerics;
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


        private List<Vector3> _lightCenters = new List<Vector3>();
        private List<float> _lightSpeeds = new List<float>();
        private List<float> _lightRadii = new List<float>();

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
            using var pool = VkCommandPool.Create(_context, new CommandPoolCreateInfo
            {
                SType = StructureType.CommandPoolCreateInfo,
                Flags = CommandPoolCreateFlags.TransientBit | CommandPoolCreateFlags.ResetCommandBufferBit
            });
            using AssimpLoader assimpLoader = new AssimpLoader(_textureStreamer);
            var meshes = await assimpLoader.LoadMeshesAsync("Resources\\Models\\SponzaAtrium\\scene.gltf", _context, pool.AllocateCommandBuffer());
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

            foreach (var item in meshes)
            {
                var entity = _world.CreateEntity();
                entity.Transform.Scale = new Vector3(0.1f);
                entity.Transform.Position = new Vector3(0);
                var mesh = entity.AddComponent<Mesh>();
                mesh.SetMeshData(item.Vertices, item.Indices);
                mesh.Material = new Material(_pipeline, item.Textures);
            }

            for (int i = 0; i < 100; i++)
            {
                var lightEntity = _world.CreateEntity();
                var light = lightEntity.AddComponent<Light>();
                var transform = lightEntity.Transform;

                // Random position within 200X200X200 cube centered at origin
                transform.Position = new Vector3(
                    (Random.Shared.NextSingle() * 200) - 100,
                    (Random.Shared.NextSingle() * 200) - 100,
                    (Random.Shared.NextSingle() * 200) - 100
                );

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
                    light.Radius = Random.Shared.NextSingle() * 20 + 5;

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
                    light.Radius = Random.Shared.NextSingle() * 15 + 5;
                    light.InnerCutoff = 0.85f;
                    light.OuterCutoff = 0.6f;

                    // Store movement parameters
                    _lightCenters.Add(transform.Position);
                    _lightSpeeds.Add(Random.Shared.NextSingle() * 1.5f + 0.5f);
                    _lightRadii.Add(Random.Shared.NextSingle() * 3 + 1);
                    cam.AddChild(lightEntity);

                }
                /*else // 10% Directional lights
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

            var colorBlendAttachments = new PipelineColorBlendAttachmentState[3];
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
                 .WithRasterizer(new VulkanRasterizerBuilder().CullFace(CullModeFlags.None))
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

        public void OnDetach()
        {
        }

        public void OnImGuiRender(VkCommandBuffer vkCommandBuffer)
        {
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
                        if (i % 10 == 0) // Every 10th spot light follows camera
                        {
                            if (camEntity != null)
                            {
                                //transform.Position = camEntity.Transform.Position;
                                light.Direction = camEntity.GetComponent<Camera>().Front;
                            }
                        }
                        else
                        {
                            center = _lightCenters[i];
                            speed = _lightSpeeds[i];
                            radius = _lightRadii[i];

                            transform.Position = center + new Vector3(
                                MathF.Cos(time * speed) * radius,
                                MathF.Sin(time * speed * 2) * radius,
                                MathF.Sin(time * speed) * radius
                            );
                            light.Direction = Vector3.Normalize(center - transform.Position);
                        }
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
