using ImGuiNET;

using RockEngine.Core;
using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Core.Rendering.ImGuiRendering;
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
        private readonly RenderingContext _context;
        private readonly GraphicsEngine _graphicsEngine;
        private readonly Renderer _renderer;
        private readonly IInputContext _inputContext;
        private readonly ImGuiController _imGuiController;
        private VkPipelineLayout _pipelineLayout;
        private VkPipeline _pipeline;

    
        public EditorLayer(World world, RenderingContext context, GraphicsEngine graphicsEngine, Renderer renderer, IInputContext inputContext)
        {
            _world = world;
            _context = context;
            _graphicsEngine = graphicsEngine;
            _renderer = renderer;
            _inputContext = inputContext;
            _imGuiController = new ImGuiController(context, graphicsEngine, _renderer.RenderPass, _inputContext, graphicsEngine.Swapchain.Extent.Width, graphicsEngine.Swapchain.Extent.Height);
        }

        public async Task OnAttach()
        {
            await CretePipeline();
            using var pool = VkCommandPool.Create(_context, new CommandPoolCreateInfo 
            { 
                SType = StructureType.CommandPoolCreateInfo,
                Flags = CommandPoolCreateFlags.TransientBit | CommandPoolCreateFlags.ResetCommandBufferBit
            });
            using AssimpLoader assimpLoader = new AssimpLoader();
            var meshes = await assimpLoader.LoadMeshesAsync("F:\\RockEngine.Vulkan\\RockEngine\\RockEngine.Editor\\Resources\\Models\\SponzaAtrium\\scene.gltf", _context, pool.AllocateCommandBuffer());
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
                mesh.Material.GeometryPipeline = _pipeline;
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

            var color_attachments = new[]
             {
                new PipelineColorBlendAttachmentState
                {
                    BlendEnable = false,
                    ColorWriteMask = ColorComponentFlags.RBit |
                                      ColorComponentFlags.GBit |
                                      ColorComponentFlags.BBit |
                                      ColorComponentFlags.ABit
                },
                new PipelineColorBlendAttachmentState
                {
                    BlendEnable = false,
                    ColorWriteMask = ColorComponentFlags.RBit |
                                      ColorComponentFlags.GBit |
                                      ColorComponentFlags.BBit |
                                      ColorComponentFlags.ABit
                },
                new PipelineColorBlendAttachmentState
                {
                    BlendEnable = false,
                    ColorWriteMask = ColorComponentFlags.RBit |
                                      ColorComponentFlags.GBit |
                                      ColorComponentFlags.BBit |
                                      ColorComponentFlags.ABit
                }
            };

            using GraphicsPipelineBuilder pipelineBuilder = new GraphicsPipelineBuilder(_context, "Main")
                 .WithShaderModule(vkShaderModuleVert)
                 .WithShaderModule(vkShaderModuleFrag)
                 .WithRasterizer(new VulkanRasterizerBuilder())
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
                     .AddAttachment(color_attachments))
                 .AddRenderPass(_renderer.GBuffer.RenderPass)
                 .WithPipelineLayout(_pipelineLayout)
                 .WithDynamicState(new PipelineDynamicStateBuilder()
                    .AddState(DynamicState.Viewport)
                    .AddState(DynamicState.Scissor)
                    )
                 .AddDepthStencilState(new PipelineDepthStencilStateCreateInfo()
                 {
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
            DrawAllocationTree();
        }
        public void OnRender(VkCommandBuffer vkCommandBuffer)
        {
            _renderer.AddCommand(new ImguiRenderCommand(_imGuiController.Render));
        }

        public void OnUpdate()
        {
            _imGuiController.Update();
        }

        private void DrawFps()
        {
            if (ImGui.Begin("FPS"))
            {
                ImGui.Text(Time.FPS.ToString());
                ImGui.End();
            }
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
            }

            ImGui.End();
        }

        private void DrawAllocationTree()
        {
            ImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Allocation Tree"))
            {
                if (ImGui.Button("Refresh Allocation Info"))
                {
                    RefreshAllocationInfo();
                }

                var groupedAllocations = _allocationInfoList.GroupBy(a => a.AllocatorType);

                if (ImGui.BeginTable("AllocationTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Sortable | ImGuiTableFlags.SortTristate))
                {
                    ImGui.TableSetupColumn("Allocator Type", ImGuiTableColumnFlags.WidthFixed, 150);
                    ImGui.TableSetupColumn("Address", ImGuiTableColumnFlags.WidthFixed, 100);
                    ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, 100);
                    ImGui.TableSetupColumn("Stack Trace", ImGuiTableColumnFlags.IndentEnable | ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableHeadersRow();

                    foreach (var group in groupedAllocations)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        if (ImGui.TreeNode(group.Key))
                        {
                            foreach (var allocationInfo in group)
                            {
                                ImGui.TableNextRow();
                                ImGui.TableSetColumnIndex(1);
                                ImGui.Text($"0x{allocationInfo.Address:X}");
                                ImGui.TableSetColumnIndex(2);
                                ImGui.Text(FormatBytes((long)allocationInfo.Size));
                                ImGui.TableSetColumnIndex(3);
                                DrawStackTraceTree(allocationInfo.StackTrace, allocationInfo.Address);
                            }
                            ImGui.TreePop();
                        }
                    }
                    ImGui.EndTable();
                }
            }
            ImGui.End();
        }



        private void DrawStackTraceTree(string stackTrace, nint address)
        {
            if (ImGui.TreeNode($"Stack Trace##{address}"))
            {
                foreach (var frame in ParseStackTrace(stackTrace))
                {
                    DrawStackFrame(frame);
                }
                ImGui.TreePop();
            }
        }

        private void DrawStackFrame(in StackFrame frame)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, frame.IsUserCode ? new Vector4(1, 1, 1, 1) : new Vector4(0.7f, 0.7f, 0.7f, 1));
            if (ImGui.TreeNode($"{frame.MethodName}##{frame.GetHashCode()}"))
            {
                ImGui.TextWrapped($"File: {frame.FileName}");
                ImGui.Text($"Line: {frame.LineNumber}");
                ImGui.TreePop();
            }

            ImGui.PopStyleColor();
        }

        private static IEnumerable<StackFrame> ParseStackTrace(string stackTrace)
        {
            return stackTrace.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                             .Select(ParseStackFrame)
                             .Where(s => s.HasValue)
                             .Select(s => s!.Value);
        }

        private static StackFrame? ParseStackFrame(string line)
        {
            var parts = line.Trim().Split(new[] { " in " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;

            var methodPart = parts[0];
            var filePart = parts[1];

            var lastColonIndex = filePart.LastIndexOf(':');
            if (lastColonIndex != -1 && int.TryParse(filePart[(lastColonIndex + 1)..].Replace("line", ""), out int lineNumber))
            {
                return new StackFrame
                {
                    MethodName = methodPart,
                    FileName = filePart[..lastColonIndex],
                    LineNumber = lineNumber,
                    IsUserCode = !filePart.Contains("System.") && !filePart.Contains("Microsoft.")
                };
            }

            return new StackFrame
            {
                MethodName = methodPart,
                FileName = filePart,
                LineNumber = 0,
                IsUserCode = !filePart.Contains("System.") && !filePart.Contains("Microsoft.")
            };
        }

        private readonly struct StackFrame
        {
            public string MethodName { get; init; }
            public string FileName { get; init; }
            public int LineNumber { get; init; }
            public bool IsUserCode { get; init; }
        }

        private void RefreshAllocationInfo()
        {
            _allocationInfoList.Clear();
            CustomAllocator.GetAllocationInfo(info =>
            {
                _allocationInfoList.Add(new AllocationInfo
                {
                    Address = info.Address,
                    Size = info.Size,
                    Scope = info.Scope,
                    StackTrace = info.StackTrace,
                    AllocatorType = info.AllocatorType
                });
            });
        }


        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return $"{size:0.##} {suffixes[order]}";
        }



        private readonly List<AllocationInfo> _allocationInfoList = new List<AllocationInfo>();
        private struct AllocationInfo
        {
            public nint Address;
            public nuint Size;
            public SystemAllocationScope Scope;
            public string StackTrace;
            public string AllocatorType;
        }

    }
}
