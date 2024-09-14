using ImGuiNET;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.ImGuiRendering;
using RockEngine.Vulkan;
using Silk.NET.Input;
using System.Numerics;
using Silk.NET.Vulkan;
using System.Text;
using RockEngine.Core;

namespace RockEngine.Editor.Layers
{
    internal class ImGuiLayer : ILayer
    {
        private readonly RenderingContext _context;
        private readonly GraphicsEngine _graphicsEngine;
        private readonly ImGuiController _imGuiController;
        private readonly List<AllocationInfo> _allocationInfoList = new List<AllocationInfo>();
        private readonly StringBuilder _stringBuilder = new StringBuilder();

        private long _totalAllocatedBytes;
        private long _peakAllocatedBytes;
        private int _totalAllocationCount;
        private int _currentActiveAllocations;

        private struct AllocationInfo
        {
            public nint Address;
            public nuint Size;
            public SystemAllocationScope Scope;
            public string StackTrace;
        }

        public ImGuiLayer(RenderingContext context, GraphicsEngine graphicsEngine, RenderPassManager renderPassManager, IInputContext input)
        {
            _context = context;
            _graphicsEngine = graphicsEngine;
            _imGuiController = new ImGuiController(context, graphicsEngine, renderPassManager, input, 800, 600);
        }

        public void OnAttach() { }

        public void OnDetach() { }

        public void OnImGuiRender(VkCommandBuffer vkCommandBuffer)
        {
            ImGui.ShowDemoWindow();
            DrawFps();
            DrawAllocationStats();
            DrawAllocationTree();
        }

        private void DrawFps()
        {
            if (ImGui.Begin("FPS"))
            {
                ImGui.Text(Time.FPS.ToString());
            }
        }

        private void DrawAllocationStats()
        {
            ImGui.Begin("Memory Allocation Diagram");

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

        private void DrawAllocationTree()
        {

            ImGui.SetNextWindowSize(new Vector2(700, 500), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Allocation Tree"))
            {
                if (ImGui.Button("Refresh Allocation Info"))
                {
                    RefreshAllocationInfo();
                }
                if (ImGui.BeginTable("AllocationTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Sortable | ImGuiTableFlags.SortTristate))
                {
                    ImGui.TableSetupColumn("Address", ImGuiTableColumnFlags.WidthFixed, 100);
                    ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, 100);
                    ImGui.TableSetupColumn("Stack Trace", ImGuiTableColumnFlags.IndentEnable | ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableHeadersRow();

                    foreach (var allocationInfo in _allocationInfoList)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.Text($"0x{allocationInfo.Address:X}");
                        ImGui.TableSetColumnIndex(1);
                        ImGui.Text(FormatBytes((long)allocationInfo.Size));
                        ImGui.TableSetColumnIndex(2);
                        DrawStackTraceTree(allocationInfo.StackTrace, allocationInfo.Address);
                    }

                    ImGui.EndTable();
                }

            }

            ImGui.EndMenuBar();
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
                    StackTrace = info.StackTrace
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

        public void OnRender(VkCommandBuffer vkCommandBuffer)
        {
            _imGuiController.Render(vkCommandBuffer, _graphicsEngine.Swapchain.Extent);
        }

        public void OnUpdate()
        {
            _imGuiController.Update();
        }
    }
}
