using ImGuiNET;

using RockEngine.Core.Diagnostics;
using RockEngine.Core.Helpers;
using RockEngine.Vulkan;

using System.Diagnostics;
using System.Numerics;
using System.Text.RegularExpressions;


namespace RockEngine.Editor.EditorUI.EditorWindows
{
    public partial class PerformanceWindow : EditorWindow
    {
        private bool _autoRefresh = true;
        private float _refreshInterval = 1.0f;
        private float _timeSinceLastRefresh = 0.0f;
        private VulkanAllocator.MemoryStatistics _currentStats;
        private string _selectedStackTrace = string.Empty;
        private bool _showStackTraceWindow = false;
        private bool _showAllocationChainsWindow = false;
        private bool _showHostAllocationsWindow = false;
        private bool _showCpuGpuPerfomanceTrace = false;
        private string _searchFilter = string.Empty;
        private float _minDurationFilter;
        private bool _showOnlySignificant;
        private readonly RingBuffer<float> _hostMemoryHistory = new(120);
        private readonly RingBuffer<float> _deviceMemoryHistory = new(120);
        private static readonly bool[] _expandedNodes = new bool[10000];


        // Store detected editors
        private static readonly Dictionary<string, (string Exe, string ArgsFormat)> _knownEditors = new()
        {
            // Visual Studio - FIRST PRIORITY
            { "devenv.exe", ("devenv.exe", "/edit \"{0}\" /command \"Edit.GoTo {1}\"") },
            // Visual Studio Code
            { "code.exe", ("code.exe", "--goto \"{0}:{1}\"") },
            { "code", ("code", "--goto \"{0}:{1}\"") },
            // Rider
            { "rider64.exe", ("rider64.exe", "\"{0}\" --line {1}") },
            { "rider", ("rider", "\"{0}\" --line {1}") },
            // Sublime Text
            { "subl.exe", ("subl.exe", "\"{0}:{1}\"") },
            { "subl", ("subl", "\"{0}:{1}\"") },
            // Notepad++
            { "notepad++.exe", ("notepad++.exe", "\"{0}\" -n{1}") },
            // VSCodium
            { "codium.exe", ("codium.exe", "--goto \"{0}:{1}\"") },
            { "codium", ("codium", "--goto \"{0}:{1}\"") }
        };

        public PerformanceWindow() : base("Performance##PerformanceWindow")
        {
            _currentStats = VulkanAllocator.GetStatistics();
        }

        protected override void OnDraw()
        {
            UpdateRefreshTimer();

            if (ImGui.CollapsingHeader("Vulkan Memory", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawMemoryStatistics();
            }

            // Draw windows if they are open
            if (_showStackTraceWindow)
            {
                DrawStackTraceWindow();
            }

            if (_showAllocationChainsWindow)
            {
                DrawAllocationChainsWindow();
            }

            if (_showHostAllocationsWindow)
            {
                DrawHostAllocationsWindow();
            }

            if (_showCpuGpuPerfomanceTrace)
            {
                DrawCpuGpuPerformanceTrace();
            }
        }

        private void DrawCpuGpuPerformanceTrace()
        {
            if (!PerformanceTracer.GPUTimestampsSupported)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "GPU Timestamps Not Supported");
            }

            ImGui.Checkbox("Only Significant", ref _showOnlySignificant);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            ImGui.DragFloat("Min Duration (ms)", ref _minDurationFilter, 0.01f, 0.01f, 100f);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200);
            ImGui.InputText("Search", ref _searchFilter, 100);

            if (ImGui.CollapsingHeader("CPU Timings", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawChildScopes(PerformanceTracer.CpuRoot.Id);
            }

            if (ImGui.CollapsingHeader("GPU Timings", ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (PerformanceTracer.GPUTimestampsSupported)
                {
                    DrawChildScopes(PerformanceTracer.GpuRoot.Id);
                }
                else
                {
                    ImGui.Text("GPU timestamps not supported on this device");
                }
            }
        }

        private void DrawChildScopes(int parentId)
        {
            if (!PerformanceTracer.ChildScopesByParentId.TryGetValue(parentId, out var children))
            {
                return;
            }

            lock (children)
            {
                foreach (int childId in children)
                {
                    var scopeID = PerformanceTracer.ScopesById[childId];
                    if (scopeID != null && scopeID.TryGetTarget(out var scope))
                    {
                        var data = PerformanceTracer.ScopeDataArray[childId];
                        if (data != null)
                        {
                            DrawScopeNode(scope, data);
                        }
                    }
                }
            }
        }

        private void DrawScopeNode(PerformanceTracer.ScopeInfo scope, PerformanceTracer.ScopeData data)
        {
            bool hasChildren = PerformanceTracer.ChildScopesByParentId.TryGetValue(scope.Id, out var children) &&
                               children.Count > 0;

            // Filtering
            bool shouldShow = !_showOnlySignificant || data.AverageDuration >= _minDurationFilter || hasChildren;
            shouldShow = shouldShow && (string.IsNullOrEmpty(_searchFilter) ||
                         scope.FullPath.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase));

            if (!shouldShow)
            {
                return;
            }

            float displayDuration = data.LastDuration > 0 ? data.LastDuration : data.AverageDuration;
            float fraction = Math.Clamp(displayDuration / 33f, 0f, 1f);

            ImGui.ProgressBar(fraction, new Vector2(100, 20), $"{displayDuration:0.00}ms");
            ImGui.SameLine();

            bool isExpanded = _expandedNodes[scope.Id];
            ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick;
            if (!hasChildren)
            {
                flags |= ImGuiTreeNodeFlags.Leaf;
            }

            bool nodeOpen = ImGui.TreeNodeEx($"{scope.Name}##{scope.Id}", flags);

            if (ImGui.IsItemClicked())
            {
                _expandedNodes[scope.Id] = !isExpanded;
            }

            if (nodeOpen)
            {
                if (hasChildren)
                {
                    ImGui.Indent(10);
                    foreach (int childId in children)
                    {
                        var scopeID = PerformanceTracer.ScopesById[childId];
                        if (scopeID != null && scopeID.TryGetTarget(out var childScope))
                        {
                            var childScopeData = PerformanceTracer.ScopeDataArray[childId];
                            if (childScopeData != null)
                            {
                                DrawScopeNode(childScope, childScopeData);
                            }
                        }
                    }
                    ImGui.Unindent(10);
                }
                ImGui.TreePop();
            }

            // Tooltip with detailed information
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text($"Full Path: {scope.FullPath}");
                ImGui.Text($"Average: {data.AverageDuration:0.00}ms");
                ImGui.Text($"Last: {data.LastDuration:0.00}ms");
                ImGui.Text($"Min: {data.MinDuration:0.00}ms");
                ImGui.Text($"Max: {data.MaxDuration:0.00}ms");
                ImGui.Text($"Samples: {data.SampleCount}");
                ImGui.EndTooltip();
            }
        }

        private void UpdateRefreshTimer()
        {
            if (_autoRefresh)
            {
                _timeSinceLastRefresh += ImGui.GetIO().DeltaTime;
                if (_timeSinceLastRefresh >= _refreshInterval)
                {
                    RefreshData();
                    _timeSinceLastRefresh = 0.0f;
                }
            }
        }

        private void RefreshData()
        {
            _currentStats = VulkanAllocator.GetStatistics();
            _hostMemoryHistory.Push(_currentStats.TotalHost / (1024f * 1024f)); // MB
            _deviceMemoryHistory.Push(_currentStats.TotalDevice / (1024f * 1024f)); // MB
        }

        private void DrawMemoryStatistics()
        {
            // Refresh controls
            ImGui.Checkbox("Auto Refresh##PerformanceAutoRefresh", ref _autoRefresh);
            ImGui.SameLine();
            if (ImGui.Button($"Refresh Now##PerformanceRefresh"))
            {
                RefreshData();
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            ImGui.DragFloat("Interval (s)##PerformanceInterval", ref _refreshInterval, 0.1f, 0.1f, 5.0f);

            ImGui.Separator();

            // Memory summary cards
            DrawSummaryCards();

            ImGui.Separator();

            // Memory graphs
            DrawMemoryGraphs();

            ImGui.Separator();

            // Resource breakdown
            DrawResourceBreakdown();

            ImGui.Separator();

            // Analysis tools
            DrawAnalysisTools();

            ImGui.Separator();

            // Actual device memory usage (new section)
            DrawActualDeviceMemoryUsage();
        }

        private void DrawAnalysisTools()
        {
            ImGui.Text("Analysis Tools:");
            ImGui.SameLine();
            if (ImGui.Button($"{Icons.Sitemap} Allocation Chains##ViewChains"))
            {
                _showAllocationChainsWindow = true;
            }
            ImGui.SameLine();
            if (ImGui.Button($"{Icons.Folder} Host Allocations##ViewHost"))
            {
                _showHostAllocationsWindow = true;
            }
            ImGui.SameLine();
            if (ImGui.Button($"{Icons.Code} CPU/GPU Timings##CPUGPUTIMINGS"))
            {
                _showCpuGpuPerfomanceTrace = true;
            }
            ImGui.SameLine();
            if (ImGui.Button($"{Icons.FileAlt} Dump to Log##DumpLog"))
            {
                VulkanAllocator.DeviceMemoryTracker.DumpAllocationChains();
            }
        }

        private void DrawAllocationChainsWindow()
        {
            ImGui.SetNextWindowSize(new Vector2(1100, 700), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Allocation Chains##AllocationChainsWindow", ref _showAllocationChainsWindow,
                ImGuiWindowFlags.NoSavedSettings))
            {
                var chains = VulkanAllocator.DeviceMemoryTracker.GetAllocationChains();

                ImGui.Text($"Allocation Chains ({chains.Length}):");
                ImGui.SameLine();
                ImGui.TextDisabled("(Shows relationships between device memory and host allocations)");

                if (chains.Length == 0)
                {
                    ImGui.Text("No allocation chains found.");
                }
                else
                {
                    if (ImGui.BeginChild("AllocationChainsScroll", new Vector2(0, -ImGui.GetFrameHeightWithSpacing() * 1.5f)))
                    {
                        for (int i = 0; i < chains.Length; i++)
                        {
                            var chain = chains[i];

                            ImGui.PushID($"Chain_{i}");

                            bool open = ImGui.TreeNode($"Chain {i}: {chain.DeviceInfo.TypeName} - {FormatSize((long)chain.DeviceInfo.AllocationSize)}");

                            // Quick view button
                            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 100);
                            if (ImGui.SmallButton($"{Icons.Eye} Quick View"))
                            {
                                _selectedStackTrace = $"=== Device Memory: {chain.DeviceInfo.TypeName} ===\n" +
                                                    $"Size: {FormatSize((long)chain.DeviceInfo.AllocationSize)}\n" +
                                                    $"Flags: {chain.DeviceInfo.MemoryPropertyFlags}\n" +
                                                    $"Call Chain: {chain.DeviceInfo.CallChain}\n" +
                                                    $"Created: {chain.CreationTime:yyyy-MM-dd HH:mm:ss.fff}\n\n" +
                                                    $"=== Stack Trace ===\n{chain.DeviceInfo.StackTrace}";
                                _showStackTraceWindow = true;
                            }

                            if (open)
                            {
                                // Device Memory Information
                                ImGui.Separator();
                                ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), "Device Memory:");
                                ImGui.Text($"Type: {chain.DeviceInfo.TypeName}");
                                ImGui.Text($"Size: {FormatSize((long)chain.DeviceInfo.AllocationSize)}");
                                ImGui.Text($"Flags: {chain.DeviceInfo.MemoryPropertyFlags}");
                                ImGui.Text($"Call Chain: {chain.DeviceInfo.CallChain}");
                                ImGui.Text($"Created: {chain.CreationTime:yyyy-MM-dd HH:mm:ss.fff}");

                                // Stack trace with file opening buttons
                                DrawStackTraceWithFileButtons(chain.DeviceInfo.StackTrace, $"DeviceStack_{i}", "Device");

                                // Associated Objects
                                var objects = VulkanAllocator.DeviceMemoryTracker.GetObjectsForDeviceMemory(chain.DeviceMemory);
                                if (objects.Length > 0)
                                {
                                    ImGui.Separator();
                                    ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), $"Associated Objects ({objects.Length}):");

                                    if (ImGui.BeginTable($"ObjectsTable_{i}", 7,
                                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
                                    {
                                        ImGui.TableSetupColumn("Type");
                                        ImGui.TableSetupColumn("Handle");
                                        ImGui.TableSetupColumn("Size");
                                        ImGui.TableSetupColumn("Offset");
                                        ImGui.TableSetupColumn("Bound");
                                        ImGui.TableSetupColumn("File");
                                        ImGui.TableSetupColumn("Actions");
                                        ImGui.TableHeadersRow();

                                        foreach (var obj in objects)
                                        {
                                            var fileInfo = ParseStackTraceForFileInfo(obj.StackTrace);

                                            ImGui.TableNextRow();

                                            ImGui.TableNextColumn();
                                            ImGui.Text(obj.Type);

                                            ImGui.TableNextColumn();
                                            ImGui.Text($"0x{obj.Handle:X}");

                                            ImGui.TableNextColumn();
                                            ImGui.Text(FormatSize((long)obj.Size));

                                            ImGui.TableNextColumn();
                                            ImGui.Text($"0x{obj.Offset:X}");

                                            ImGui.TableNextColumn();
                                            ImGui.Text($"{obj.BindingTime:HH:mm:ss.fff}");

                                            ImGui.TableNextColumn();
                                            if (!string.IsNullOrEmpty(fileInfo.FilePath))
                                            {
                                                ImGui.Text(Path.GetFileName(fileInfo.FilePath));
                                            }
                                            else
                                            {
                                                ImGui.TextDisabled("N/A");
                                            }

                                            ImGui.TableNextColumn();
                                            DrawStackTraceWithFileButtons(obj.StackTrace, $"Object_{obj.Handle}", "Object");
                                        }

                                        ImGui.EndTable();
                                    }
                                }

                                // Host Sources
                                if (chain.HostSources.Count > 0)
                                {
                                    ImGui.Separator();
                                    ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"Host Sources ({chain.HostSources.Count}):");

                                    if (ImGui.BeginTable($"HostSourcesTable_{i}", 8,
                                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
                                    {
                                        ImGui.TableSetupColumn("Type");
                                        ImGui.TableSetupColumn("Pointer");
                                        ImGui.TableSetupColumn("Size");
                                        ImGui.TableSetupColumn("Time");
                                        ImGui.TableSetupColumn("File");
                                        ImGui.TableSetupColumn("Line");
                                        ImGui.TableSetupColumn("View");
                                        ImGui.TableSetupColumn("Open");
                                        ImGui.TableHeadersRow();

                                        for (int j = 0; j < chain.HostSources.Count; j++)
                                        {
                                            var host = chain.HostSources[j];
                                            var fileInfo = ParseStackTraceForFileInfo(host.StackTrace);

                                            ImGui.TableNextRow();

                                            ImGui.TableNextColumn();
                                            ImGui.Text(host.TypeName);

                                            ImGui.TableNextColumn();
                                            ImGui.Text($"0x{host.HostPtr:X}");

                                            ImGui.TableNextColumn();
                                            ImGui.Text(FormatSize(host.Size));

                                            ImGui.TableNextColumn();
                                            ImGui.Text($"{host.AllocationTime:HH:mm:ss.fff}");

                                            ImGui.TableNextColumn();
                                            if (!string.IsNullOrEmpty(fileInfo.FilePath))
                                            {
                                                ImGui.Text(Path.GetFileName(fileInfo.FilePath));
                                            }
                                            else
                                            {
                                                ImGui.TextDisabled("N/A");
                                            }

                                            ImGui.TableNextColumn();
                                            if (fileInfo.LineNumber > 0)
                                            {
                                                ImGui.Text($"{fileInfo.LineNumber}");
                                            }
                                            else
                                            {
                                                ImGui.TextDisabled("N/A");
                                            }

                                            ImGui.TableNextColumn();
                                            if (ImGui.SmallButton($"{Icons.Eye}##Host_{i}_{j}"))
                                            {
                                                _selectedStackTrace = host.StackTrace;
                                                _showStackTraceWindow = true;
                                            }

                                            ImGui.TableNextColumn();
                                            if (!string.IsNullOrEmpty(fileInfo.FilePath) && File.Exists(fileInfo.FilePath))
                                            {
                                                if (ImGui.SmallButton($"{Icons.FileCode}##OpenHost_{i}_{j}"))
                                                {
                                                    OpenFileInEditor(fileInfo.FilePath, fileInfo.LineNumber);
                                                }
                                                ImGui.SameLine(0, 2);
                                                if (ImGui.SmallButton($"{Icons.FolderOpen}##FolderHost_{i}_{j}"))
                                                {
                                                    OpenFileFolder(fileInfo.FilePath);
                                                }
                                            }
                                        }

                                        ImGui.EndTable();
                                    }
                                }
                                else
                                {
                                    ImGui.TextDisabled("No host allocations linked to this device memory.");
                                }

                                ImGui.TreePop();
                            }

                            ImGui.PopID();
                        }
                        ImGui.EndChild();
                    }

                    ImGui.Separator();
                    ImGui.Text($"Total Chains: {chains.Length}");
                }

                ImGui.Separator();
                if (ImGui.Button($"{Icons.Times} Close##CloseAllocationChains"))
                {
                    _showAllocationChainsWindow = false;
                }

            }
            ImGui.End();
        }

        private void DrawHostAllocationsWindow()
        {
            ImGui.SetNextWindowSize(new Vector2(1000, 600), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Host Allocations##HostAllocationsWindow", ref _showHostAllocationsWindow,
                ImGuiWindowFlags.NoSavedSettings))
            {
                // Get all device memories
                var deviceMemories = VulkanAllocator.DeviceMemoryTracker.GetDeviceMemoryDetails();

                ImGui.Text($"Device Memory Allocations ({deviceMemories.Length}):");
                ImGui.SameLine();
                ImGui.TextDisabled("(Shows host allocations for each device memory)");

                if (deviceMemories.Length == 0)
                {
                    ImGui.Text("No device memory allocations found.");
                }
                else
                {
                    if (ImGui.BeginChild("HostAllocationsScroll", new Vector2(0, -ImGui.GetFrameHeightWithSpacing() * 1.5f)))
                    {
                        foreach (var deviceMemory in deviceMemories)
                        {
                            ImGui.PushID((nint)deviceMemory.DeviceMemory.Handle);
                            var hostAllocs = VulkanAllocator.DeviceMemoryTracker.GetHostAllocationsForDeviceMemory(deviceMemory.DeviceMemory);

                            bool open = ImGui.TreeNode($"{deviceMemory.TypeName}: {FormatSize((long)deviceMemory.AllocationSize)} ({hostAllocs.Length} host allocations)");

                            if (open)
                            {
                                ImGui.Text($"Device Memory: {deviceMemory.TypeName}");
                                ImGui.Text($"Size: {FormatSize((long)deviceMemory.AllocationSize)}");
                                ImGui.Text($"Flags: {deviceMemory.MemoryPropertyFlags}");

                                // Device memory stack trace with file buttons
                                DrawStackTraceWithFileButtons(deviceMemory.StackTrace, $"DeviceStack_{deviceMemory.DeviceMemory.Handle}", "Device");

                                if (hostAllocs.Length > 0)
                                {
                                    ImGui.Separator();
                                    ImGui.Text("Linked Host Allocations:");

                                    if (ImGui.BeginTable($"HostAllocs_{deviceMemory.DeviceMemory.Handle}", 8,
                                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
                                    {
                                        ImGui.TableSetupColumn("Type");
                                        ImGui.TableSetupColumn("Pointer");
                                        ImGui.TableSetupColumn("Size");
                                        ImGui.TableSetupColumn("Allocated");
                                        ImGui.TableSetupColumn("File");
                                        ImGui.TableSetupColumn("Line");
                                        ImGui.TableSetupColumn("View");
                                        ImGui.TableSetupColumn("Open");
                                        ImGui.TableHeadersRow();

                                        foreach (var host in hostAllocs)
                                        {
                                            var (FilePath, LineNumber) = ParseStackTraceForFileInfo(host.StackTrace);

                                            ImGui.TableNextRow();

                                            ImGui.TableNextColumn();
                                            ImGui.Text(host.TypeName);

                                            ImGui.TableNextColumn();
                                            ImGui.Text($"0x{host.HostPtr:X}");

                                            ImGui.TableNextColumn();
                                            ImGui.Text(FormatSize(host.Size));

                                            ImGui.TableNextColumn();
                                            ImGui.Text($"{host.AllocationTime:HH:mm:ss.fff}");

                                            ImGui.TableNextColumn();
                                            if (!string.IsNullOrEmpty(FilePath))
                                            {
                                                ImGui.Text(Path.GetFileName(FilePath));
                                            }
                                            else
                                            {
                                                ImGui.TextDisabled("N/A");
                                            }

                                            ImGui.TableNextColumn();
                                            if (LineNumber > 0)
                                            {
                                                ImGui.Text($"{LineNumber}");
                                            }
                                            else
                                            {
                                                ImGui.TextDisabled("N/A");
                                            }

                                            ImGui.TableNextColumn();
                                            if (ImGui.SmallButton($"{Icons.Eye}##HostTrace_{host.HostPtr}"))
                                            {
                                                _selectedStackTrace = host.StackTrace;
                                                _showStackTraceWindow = true;
                                            }

                                            ImGui.TableNextColumn();
                                            if (!string.IsNullOrEmpty(FilePath) && File.Exists(FilePath))
                                            {
                                                if (ImGui.SmallButton($"{Icons.FileCode}##OpenHostFile_{host.HostPtr}"))
                                                {
                                                    OpenFileInEditor(FilePath, LineNumber);
                                                }
                                                ImGui.SameLine(0, 2);
                                                if (ImGui.SmallButton($"{Icons.FolderOpen}##OpenHostFolder_{host.HostPtr}"))
                                                {
                                                    OpenFileFolder(FilePath);
                                                }
                                            }
                                        }

                                        ImGui.EndTable();
                                    }
                                }
                                else
                                {
                                    ImGui.TextDisabled("No host allocations linked.");
                                }

                                ImGui.TreePop();
                            }
                            ImGui.PopID();
                        }
                        ImGui.EndChild();
                    }

                    ImGui.Separator();
                    ImGui.Text($"Total Device Memories: {deviceMemories.Length}");
                }

                ImGui.Separator();
                if (ImGui.Button($"{Icons.Times} Close##CloseHostAllocations"))
                {
                    _showHostAllocationsWindow = false;
                }

                ImGui.End();
            }
        }

        private void DrawStackTraceWindow()
        {
            ImGui.SetNextWindowSize(new Vector2(1000, 700), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Stack Trace##StackTraceWindow", ref _showStackTraceWindow,
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.HorizontalScrollbar))
            {
                if (!string.IsNullOrEmpty(_selectedStackTrace))
                {
                    // Top buttons
                    if (ImGui.Button($"{Icons.Copy} Copy to Clipboard##CopyStackTrace"))
                    {
                        ImGui.SetClipboardText(_selectedStackTrace);
                    }
                    ImGui.SameLine();

                    if (ImGui.Button($"{Icons.Times} Clear##ClearStackTrace"))
                    {
                        _selectedStackTrace = string.Empty;
                    }

                    // Parse and add file buttons
                    var fileInfos = ParseStackTraceForAllFiles(_selectedStackTrace);
                    if (fileInfos.Count > 0)
                    {
                        ImGui.SameLine();
                        ImGui.TextDisabled($"Found {fileInfos.Count} source file(s):");

                        foreach (var fileInfo in fileInfos)
                        {
                            ImGui.SameLine();
                            string fileName = Path.GetFileName(fileInfo.FilePath);
                            string buttonText = fileInfo.LineNumber > 0
                                ? $"{Icons.FileCode} {fileName}:{fileInfo.LineNumber}"
                                : $"{Icons.FileCode} {fileName}";

                            if (ImGui.SmallButton(buttonText))
                            {
                                OpenFileInEditor(fileInfo.FilePath, fileInfo.LineNumber);
                            }
                            ImGui.SameLine(0, 2);
                            if (ImGui.SmallButton($"{Icons.FolderOpen}##{fileInfo.GetHashCode()}"))
                            {
                                OpenFileFolder(fileInfo.FilePath);
                            }
                        }
                    }

                    ImGui.Separator();

                    // Display the stack trace with clickable file links
                    if (ImGui.BeginChild("StackTraceContent", Vector2.Zero, ImGuiChildFlags.None,
                        ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.AlwaysVerticalScrollbar))
                    {
                        var lines = _selectedStackTrace.Split('\n');
                        foreach (var line in lines)
                        {
                            var fileInfo = ParseStackTraceLineForFileInfo(line);
                            if (!string.IsNullOrEmpty(fileInfo.FilePath))
                            {
                                // Line with file info - make it clickable
                                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.8f, 1.0f, 1.0f));
                                ImGui.TextUnformatted(line);
                                ImGui.PopStyleColor();

                                // Make the entire line clickable
                                if (ImGui.IsItemClicked())
                                {
                                    OpenFileInEditor(fileInfo.FilePath, fileInfo.LineNumber);
                                }

                                // Add a tooltip
                                if (ImGui.IsItemHovered())
                                {
                                    ImGui.BeginTooltip();
                                    ImGui.Text($"Click to open: {Path.GetFileName(fileInfo.FilePath)}:line {fileInfo.LineNumber}");
                                    ImGui.EndTooltip();
                                }

                                // Same line buttons for file and folder
                                ImGui.SameLine(ImGui.GetContentRegionAvail().X - 60);
                                if (ImGui.SmallButton($"{Icons.FileCode}##Open_{fileInfo.GetHashCode()}"))
                                {
                                    OpenFileInEditor(fileInfo.FilePath, fileInfo.LineNumber);
                                }
                                ImGui.SameLine(0, 2);
                                if (ImGui.SmallButton($"{Icons.FolderOpen}##Folder_{fileInfo.GetHashCode()}"))
                                {
                                    OpenFileFolder(fileInfo.FilePath);
                                }
                            }
                            else
                            {
                                // Regular line without file info
                                ImGui.TextUnformatted(line);
                            }
                        }
                        ImGui.EndChild();
                    }
                }
                else
                {
                    ImGui.Text("No stack trace selected.");
                }

                ImGui.End();
            }
        }

        private void DrawStackTraceWithFileButtons(string stackTrace, string uniqueId, string label = "View")
        {
            if (string.IsNullOrEmpty(stackTrace))
                return;

            // View button
            if (ImGui.SmallButton($"{Icons.Eye} {label}##{uniqueId}"))
            {
                _selectedStackTrace = stackTrace;
                _showStackTraceWindow = true;
            }

            // Parse for file info
            var fileInfo = ParseStackTraceForFileInfo(stackTrace);

            if (!string.IsNullOrEmpty(fileInfo.FilePath) && File.Exists(fileInfo.FilePath))
            {
                ImGui.SameLine();

                // Open file button with line number
                string fileName = Path.GetFileName(fileInfo.FilePath);
                string buttonText = fileInfo.LineNumber > 0
                    ? $"{Icons.FileCode} {fileName}:{fileInfo.LineNumber}"
                    : $"{Icons.FileCode} {fileName}";

                if (ImGui.SmallButton($"{buttonText}##Open_{uniqueId}"))
                {
                    OpenFileInEditor(fileInfo.FilePath, fileInfo.LineNumber);
                }

                ImGui.SameLine(0, 2);

                // Open folder button
                if (ImGui.SmallButton($"{Icons.FolderOpen}##Folder_{uniqueId}"))
                {
                    OpenFileFolder(fileInfo.FilePath);
                }
            }
        }

        private void DrawActualDeviceMemoryUsage()
        {
            if (ImGui.TreeNode("Actual Device Memory Usage##DeviceMemoryDetails"))
            {
                long actualDeviceMemory = VulkanAllocator.DeviceMemoryTracker.GetActualDeviceMemoryUsage();
                long actualHostMemory = VulkanAllocator.DeviceMemoryTracker.GetActualHostMemoryUsage();

                ImGui.Text($"Actual VRAM Usage: {FormatSize(actualDeviceMemory)}");
                ImGui.Text($"Actual RAM Usage: {FormatSize(Environment.WorkingSet)}");

                // Device memory objects section
                DrawDeviceMemoryObjects();

                // Raw device memory details
                var details = VulkanAllocator.DeviceMemoryTracker.GetDeviceMemoryDetails();
                if (details.Length > 0)
                {
                    ImGui.Separator();
                    if (ImGui.TreeNode($"Raw Device Memory Details ({details.Length} allocations)##RawDeviceMemory"))
                    {
                        if (ImGui.BeginTable("DeviceMemoryDetailsTable", 9,
                            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
                        {
                            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthStretch);
                            ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, 100);
                            ImGui.TableSetupColumn("Memory Flags", ImGuiTableColumnFlags.WidthFixed, 150);
                            ImGui.TableSetupColumn("Call Chain", ImGuiTableColumnFlags.WidthStretch);
                            ImGui.TableSetupColumn("Stack Depth", ImGuiTableColumnFlags.WidthFixed, 80);
                            ImGui.TableSetupColumn("File", ImGuiTableColumnFlags.WidthFixed, 150);
                            ImGui.TableSetupColumn("Line", ImGuiTableColumnFlags.WidthFixed, 60);
                            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 150);
                            ImGui.TableSetupColumn("Open", ImGuiTableColumnFlags.WidthFixed, 80);
                            ImGui.TableHeadersRow();

                            foreach (var detail in details)
                            {
                                var fileInfo = ParseStackTraceForFileInfo(detail.StackTrace);

                                ImGui.TableNextRow();

                                ImGui.TableNextColumn();
                                ImGui.Text(detail.TypeName);

                                ImGui.TableNextColumn();
                                ImGui.Text(FormatSize((long)detail.AllocationSize));

                                ImGui.TableNextColumn();
                                ImGui.Text(detail.MemoryPropertyFlags.ToString());

                                ImGui.TableNextColumn();
                                ImGui.TextWrapped(detail.CallChain);

                                ImGui.TableNextColumn();
                                int stackDepth = string.IsNullOrEmpty(detail.StackTrace) ? 0 :
                                                detail.StackTrace.Count(c => c == '\n');
                                ImGui.Text($"{stackDepth} lines");

                                ImGui.TableNextColumn();
                                if (!string.IsNullOrEmpty(fileInfo.FilePath))
                                {
                                    ImGui.Text(Path.GetFileName(fileInfo.FilePath));
                                }
                                else
                                {
                                    ImGui.TextDisabled("N/A");
                                }

                                ImGui.TableNextColumn();
                                if (fileInfo.LineNumber > 0)
                                {
                                    ImGui.Text($"{fileInfo.LineNumber}");
                                }
                                else
                                {
                                    ImGui.TextDisabled("N/A");
                                }

                                ImGui.TableNextColumn();
                                if (ImGui.SmallButton($"{Icons.Eye} View##View_{detail.GetHashCode()}"))
                                {
                                    _selectedStackTrace = detail.StackTrace;
                                    _showStackTraceWindow = true;
                                }

                                ImGui.TableNextColumn();
                                if (!string.IsNullOrEmpty(fileInfo.FilePath) && File.Exists(fileInfo.FilePath))
                                {
                                    if (ImGui.SmallButton($"{Icons.FileCode}##Open_{detail.GetHashCode()}"))
                                    {
                                        OpenFileInEditor(fileInfo.FilePath, fileInfo.LineNumber);
                                    }
                                    ImGui.SameLine(0, 2);
                                    if (ImGui.SmallButton($"{Icons.FolderOpen}##Folder_{detail.GetHashCode()}"))
                                    {
                                        OpenFileFolder(fileInfo.FilePath);
                                    }
                                }
                            }

                            ImGui.EndTable();
                        }
                        ImGui.TreePop();
                    }
                }
                else
                {
                    ImGui.TextDisabled("No device memory details available.");
                }

                ImGui.TreePop();
            }
        }

        private void DrawDeviceMemoryObjects()
        {
            var allObjects = VulkanAllocator.DeviceMemoryTracker.GetAllDeviceMemoryObjects();

            if (ImGui.TreeNode($"Device Memory Objects ({allObjects.Count} allocations)##DeviceMemoryObjects"))
            {
                if (allObjects.Count == 0)
                {
                    ImGui.TextDisabled("No device memory objects found.");
                }
                else
                {
                    foreach (var (deviceMemory, objects) in allObjects)
                    {
                        if (objects.Length == 0) continue;

                        var deviceInfo = VulkanAllocator.DeviceMemoryTracker.GetDeviceMemoryDetails()
                            .FirstOrDefault(info => info.DeviceMemory.Handle == deviceMemory.Handle);

                        if (deviceInfo == null) continue;

                        if (ImGui.TreeNode($"{deviceInfo.TypeName}: {FormatSize((long)deviceInfo.AllocationSize)} ({objects.Length} objects)"))
                        {
                            ImGui.Text($"Memory Flags: {deviceInfo.MemoryPropertyFlags}");

                            // Device memory actions with file buttons
                            DrawStackTraceWithFileButtons(deviceInfo.StackTrace, $"DeviceStack_{deviceMemory.Handle}", "Device");

                            // Associated host allocations
                            var hostAllocs = VulkanAllocator.DeviceMemoryTracker.GetHostAllocationsForDeviceMemory(deviceMemory);
                            if (hostAllocs.Length > 0)
                            {
                                ImGui.Separator();
                                if (ImGui.TreeNode($"Linked Host Allocations ({hostAllocs.Length})##HostAllocs_{deviceMemory.Handle}"))
                                {
                                    if (ImGui.BeginTable($"HostAllocsTable_{deviceMemory.Handle}", 9,
                                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
                                    {
                                        ImGui.TableSetupColumn("Type");
                                        ImGui.TableSetupColumn("Pointer");
                                        ImGui.TableSetupColumn("Size");
                                        ImGui.TableSetupColumn("Allocated");
                                        ImGui.TableSetupColumn("File");
                                        ImGui.TableSetupColumn("Line");
                                        ImGui.TableSetupColumn("View");
                                        ImGui.TableSetupColumn("Open File");
                                        ImGui.TableSetupColumn("Open Folder");
                                        ImGui.TableHeadersRow();

                                        foreach (var host in hostAllocs)
                                        {
                                            var fileInfo = ParseStackTraceForFileInfo(host.StackTrace);

                                            ImGui.TableNextRow();

                                            ImGui.TableNextColumn();
                                            ImGui.Text(host.TypeName);

                                            ImGui.TableNextColumn();
                                            ImGui.Text($"0x{host.HostPtr:X}");

                                            ImGui.TableNextColumn();
                                            ImGui.Text(FormatSize(host.Size));

                                            ImGui.TableNextColumn();
                                            ImGui.Text($"{host.AllocationTime:HH:mm:ss.fff}");

                                            ImGui.TableNextColumn();
                                            if (!string.IsNullOrEmpty(fileInfo.FilePath))
                                            {
                                                ImGui.Text(Path.GetFileName(fileInfo.FilePath));
                                            }
                                            else
                                            {
                                                ImGui.TextDisabled("N/A");
                                            }

                                            ImGui.TableNextColumn();
                                            if (fileInfo.LineNumber > 0)
                                            {
                                                ImGui.Text($"{fileInfo.LineNumber}");
                                            }
                                            else
                                            {
                                                ImGui.TextDisabled("N/A");
                                            }

                                            ImGui.TableNextColumn();
                                            if (ImGui.SmallButton($"{Icons.Eye}##HostView_{host.HostPtr}"))
                                            {
                                                _selectedStackTrace = host.StackTrace;
                                                _showStackTraceWindow = true;
                                            }

                                            ImGui.TableNextColumn();
                                            if (!string.IsNullOrEmpty(fileInfo.FilePath) && File.Exists(fileInfo.FilePath))
                                            {
                                                if (ImGui.SmallButton($"{Icons.FileCode}##OpenHost_{host.HostPtr}"))
                                                {
                                                    OpenFileInEditor(fileInfo.FilePath, fileInfo.LineNumber);
                                                }
                                            }

                                            ImGui.TableNextColumn();
                                            if (!string.IsNullOrEmpty(fileInfo.FilePath) && File.Exists(fileInfo.FilePath))
                                            {
                                                if (ImGui.SmallButton($"{Icons.FolderOpen}##FolderHost_{host.HostPtr}"))
                                                {
                                                    OpenFileFolder(fileInfo.FilePath);
                                                }
                                            }
                                        }

                                        ImGui.EndTable();
                                    }
                                    ImGui.TreePop();
                                }
                            }

                            if (objects.Length > 0)
                            {
                                ImGui.Separator();
                                ImGui.Text($"Associated Vulkan Objects ({objects.Length}):");

                                if (ImGui.BeginTable($"Objects_{deviceMemory.Handle}", 9,
                                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
                                {
                                    ImGui.TableSetupColumn("Type");
                                    ImGui.TableSetupColumn("Handle");
                                    ImGui.TableSetupColumn("Size");
                                    ImGui.TableSetupColumn("Offset");
                                    ImGui.TableSetupColumn("Bound");
                                    ImGui.TableSetupColumn("File");
                                    ImGui.TableSetupColumn("Line");
                                    ImGui.TableSetupColumn("Actions");
                                    ImGui.TableSetupColumn("Open");
                                    ImGui.TableHeadersRow();

                                    foreach (var obj in objects)
                                    {
                                        var fileInfo = ParseStackTraceForFileInfo(obj.StackTrace);

                                        ImGui.TableNextRow();

                                        ImGui.TableNextColumn();
                                        ImGui.Text(obj.Type);

                                        ImGui.TableNextColumn();
                                        ImGui.Text($"0x{obj.Handle:X}");

                                        ImGui.TableNextColumn();
                                        ImGui.Text(FormatSize((long)obj.Size));

                                        ImGui.TableNextColumn();
                                        ImGui.Text($"0x{obj.Offset:X}");

                                        ImGui.TableNextColumn();
                                        ImGui.Text($"{obj.BindingTime:HH:mm:ss.fff}");

                                        ImGui.TableNextColumn();
                                        if (!string.IsNullOrEmpty(fileInfo.FilePath))
                                        {
                                            ImGui.Text(Path.GetFileName(fileInfo.FilePath));
                                        }
                                        else
                                        {
                                            ImGui.TextDisabled("N/A");
                                        }

                                        ImGui.TableNextColumn();
                                        if (fileInfo.LineNumber > 0)
                                        {
                                            ImGui.Text($"{fileInfo.LineNumber}");
                                        }
                                        else
                                        {
                                            ImGui.TextDisabled("N/A");
                                        }

                                        ImGui.TableNextColumn();
                                        DrawStackTraceWithFileButtons(obj.StackTrace, $"Obj_{obj.Handle}", "View");

                                        ImGui.TableNextColumn();
                                        if (!string.IsNullOrEmpty(fileInfo.FilePath) && File.Exists(fileInfo.FilePath))
                                        {
                                            if (ImGui.SmallButton($"{Icons.FileCode}##OpenObj_{obj.Handle}"))
                                            {
                                                OpenFileInEditor(fileInfo.FilePath, fileInfo.LineNumber);
                                            }
                                            ImGui.SameLine(0, 2);
                                            if (ImGui.SmallButton($"{Icons.FolderOpen}##FolderObj_{obj.Handle}"))
                                            {
                                                OpenFileFolder(fileInfo.FilePath);
                                            }
                                        }
                                    }

                                    ImGui.EndTable();
                                }
                            }

                            ImGui.TreePop();
                        }
                    }
                }

                ImGui.TreePop();
            }
        }

        // Helper methods for parsing stack traces and opening files
        private (string FilePath, int LineNumber) ParseStackTraceLineForFileInfo(string line)
        {
            try
            {
                var match = StackFrameRegex().Match(line);
                if (match.Success && match.Groups.Count >= 3)
                {
                    string filePath = match.Groups[2].Value.Trim();
                    if (int.TryParse(match.Groups[3].Value, out int lineNumber))
                    {
                        // Clean up the file path
                        filePath = filePath.Trim(' ', '\t', '\r', '\n', '\"', '\'');

                        // Check if it's a valid path
                        if (IsValidPath(filePath))
                        {
                            return (filePath, lineNumber);
                        }
                    }
                }

                // Try alternative format
                var simpleMatch = SimpleFileLineRegex().Match(line);
                if (simpleMatch.Success)
                {
                    // Extract file path manually
                    int lineIndex = line.IndexOf(":line ", StringComparison.OrdinalIgnoreCase);
                    if (lineIndex > 0)
                    {
                        int fileStart = line.LastIndexOf(" in ", lineIndex, StringComparison.OrdinalIgnoreCase);
                        if (fileStart > 0)
                        {
                            string filePath = line.Substring(fileStart + 4, lineIndex - fileStart - 4).Trim();
                            filePath = filePath.Trim(' ', '\t', '\r', '\n', '\"', '\'');

                            if (IsValidPath(filePath) && int.TryParse(simpleMatch.Groups[1].Value, out int lineNumber))
                            {
                                return (filePath, lineNumber);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return (string.Empty, 0);
        }

        private (string FilePath, int LineNumber) ParseStackTraceForFileInfo(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace))
                return (string.Empty, 0);

            var lines = stackTrace.Split('\n');
            foreach (var line in lines)
            {
                var info = ParseStackTraceLineForFileInfo(line);
                if (!string.IsNullOrEmpty(info.FilePath))
                    return info;
            }

            return (string.Empty, 0);
        }

        private List<(string FilePath, int LineNumber)> ParseStackTraceForAllFiles(string stackTrace)
        {
            var result = new List<(string, int)>();

            if (string.IsNullOrEmpty(stackTrace))
                return result;

            var lines = stackTrace.Split('\n');
            foreach (var line in lines)
            {
                var info = ParseStackTraceLineForFileInfo(line);
                if (!string.IsNullOrEmpty(info.FilePath))
                {
                    result.Add(info);
                }
            }

            return result;
        }

        private bool IsValidPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                // Check for Windows path
                if (WindowsPathRegex().IsMatch(path))
                    return true;

                // Check for Unix path
                if (UnixPathRegex().IsMatch(path))
                    return true;

                // Check if path exists
                return File.Exists(path) || Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private void OpenFileInEditor(string filePath, int lineNumber = 0)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    // Try to find the file in the solution
                    string fileName = Path.GetFileName(filePath);
                    var solutionDir = FindSolutionDirectory();

                    if (!string.IsNullOrEmpty(solutionDir))
                    {
                        var foundFiles = Directory.GetFiles(solutionDir, fileName, SearchOption.AllDirectories);
                        if (foundFiles.Length > 0)
                        {
                            filePath = foundFiles[0];
                        }
                        else
                        {
                            // Try with just the relative path portion
                            var relativeMatch = System.Text.RegularExpressions.Regex.Match(filePath, @"[\\/](?:[^\\/]+[\\/])*[^\\/]+$");
                            if (relativeMatch.Success)
                            {
                                string relativePath = relativeMatch.Value.TrimStart('\\', '/');
                                foundFiles = Directory.GetFiles(solutionDir, relativePath, SearchOption.AllDirectories);
                                if (foundFiles.Length > 0)
                                {
                                    filePath = foundFiles[0];
                                }
                            }
                        }
                    }
                }

                if (!File.Exists(filePath))
                {
                    // File not found - open containing folder if possible
                    OpenFileFolder(Path.GetDirectoryName(filePath));
                    return;
                }

                // Try editors in the order they appear in _knownEditors
                foreach (var editor in _knownEditors.Values)
                {
                    try
                    {
                        string exePath = FindExecutable(editor.Exe);
                        if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                        {
                            string arguments = string.Format(editor.ArgsFormat, filePath, lineNumber);

                            var psi = new ProcessStartInfo
                            {
                                FileName = exePath,
                                Arguments = arguments,
                                UseShellExecute = true
                            };

                            Process.Start(psi);
                            return; // Successfully opened with editor
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to open with {editor.Exe}: {ex.Message}");
                        // Try next editor
                    }
                }

                // If no editor worked, try to open with default application
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    };

                    Process.Start(startInfo);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to open file: {ex.Message}");
                    // Last resort: open folder
                    OpenFileFolder(filePath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening file {filePath}: {ex.Message}");
                // Last resort: try to open folder
                try
                {
                    string dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        OpenFileFolder(dir);
                    }
                }
                catch
                {
                    // Ignore
                }
            }
        }



        private string FindExecutable(string exeName)
        {
            // First try direct path
            if (File.Exists(exeName))
                return Path.GetFullPath(exeName);

            // Check PATH environment variable
            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                var pathDirs = pathEnv.Split(Path.PathSeparator);
                foreach (var dir in pathDirs)
                {
                    try
                    {
                        string fullPath = Path.Combine(dir, exeName);
                        if (File.Exists(fullPath))
                            return fullPath;
                    }
                    catch
                    {
                        // Invalid path, continue
                    }
                }
            }

            return null;
        }


        private void OpenFileFolder(string filePath)
        {
            try
            {
                string directoryPath;

                if (File.Exists(filePath))
                {
                    directoryPath = Path.GetDirectoryName(filePath);
                }
                else if (Directory.Exists(filePath))
                {
                    directoryPath = filePath;
                }
                else
                {
                    // Try to get the directory from the path
                    directoryPath = Path.GetDirectoryName(filePath);
                    if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
                    {
                        // Try to find the file in the solution
                        string fileName = Path.GetFileName(filePath);
                        var solutionDir = FindSolutionDirectory();

                        if (!string.IsNullOrEmpty(solutionDir))
                        {
                            var foundFiles = Directory.GetFiles(solutionDir, fileName, SearchOption.AllDirectories);
                            if (foundFiles.Length > 0)
                            {
                                directoryPath = Path.GetDirectoryName(foundFiles[0]);
                            }
                            else
                            {
                                // Just open the solution directory
                                directoryPath = solutionDir;
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(directoryPath) && Directory.Exists(directoryPath))
                {
                    // Open the folder in file explorer
                    if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    {
                        Process.Start("explorer.exe", $"\"{directoryPath}\"");
                    }
                    else if (Environment.OSVersion.Platform == PlatformID.Unix)
                    {
                        Process.Start("xdg-open", $"\"{directoryPath}\"");
                    }
                    else if (Environment.OSVersion.Platform == PlatformID.MacOSX)
                    {
                        Process.Start("open", $"\"{directoryPath}\"");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening folder for {filePath}: {ex.Message}");
            }
        }

        private string FindSolutionDirectory()
        {
            try
            {
                // Start from current directory and go up until we find a .sln file
                string currentDir = Directory.GetCurrentDirectory();
                string dir = currentDir;

                while (!string.IsNullOrEmpty(dir))
                {
                    var slnFiles = Directory.GetFiles(dir, "*.sln");
                    if (slnFiles.Length > 0)
                        return dir;

                    var parent = Directory.GetParent(dir);
                    if (parent == null)
                        break;

                    dir = parent.FullName;
                }

                // Fall back to current directory
                return currentDir;
            }
            catch
            {
                return Directory.GetCurrentDirectory();
            }
        }

        private void DrawSummaryCards()
        {
            // Host Memory Card
            ImGui.BeginGroup();
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "HOST MEMORY");
            ImGui.Text($"Current: {FormatSize(_currentStats.TotalHost)}");
            ImGui.Text($"Peak: {FormatSize(_currentStats.PeakHost)}");
            ImGui.Text($"Allocations: {_currentStats.ActiveHostAllocations}");
            ImGui.EndGroup();

            ImGui.SameLine(ImGui.GetWindowWidth() * 0.33f);

            // Device Memory Card
            ImGui.BeginGroup();
            ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), "DEVICE MEMORY");
            ImGui.Text($"Current: {FormatSize(_currentStats.TotalDevice)}");
            ImGui.Text($"Peak: {FormatSize(_currentStats.PeakDevice)}");
            ImGui.Text($"Allocations: {_currentStats.ActiveDeviceAllocations}");
            ImGui.EndGroup();

            ImGui.SameLine(ImGui.GetWindowWidth() * 0.66f);

            // Total Memory Card
            ImGui.BeginGroup();
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.2f, 1.0f), "TOTAL MEMORY");
            ImGui.Text($"{FormatSize(_currentStats.TotalMemory)}");
            ImGui.Text($"{_currentStats.ActiveHostAllocations + _currentStats.ActiveDeviceAllocations} allocations");
            ImGui.Text($"{(_currentStats.TotalDevice / (float)_currentStats.TotalMemory * 100):F1}% VRAM");
            ImGui.EndGroup();
        }

        private void DrawMemoryGraphs()
        {
            ImGui.Text("Memory Usage Over Time:");

            // Host memory graph
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 0.8f), "Host Memory:");
            if (_hostMemoryHistory.Count > 0)
            {
                float max = _hostMemoryHistory.Max();
                float min = _hostMemoryHistory.Min();
                float current = _hostMemoryHistory.Last();
                ImGui.Text($"Current: {current:F1} MB | Min: {min:F1} MB | Max: {max:F1} MB");
                var hostArray = _hostMemoryHistory.ToArray();
                if (hostArray.Length > 0)
                {
                    ImGui.PlotLines("##HostMemoryGraph", ref hostArray[0], _hostMemoryHistory.Count, 0,
                        null, min, max, new Vector2(ImGui.GetContentRegionAvail().X, 40));
                }
            }

            // Device memory graph
            ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 0.8f), "Device Memory:");
            if (_deviceMemoryHistory.Count > 0)
            {
                float max = _deviceMemoryHistory.Max();
                float min = _deviceMemoryHistory.Min();
                float current = _deviceMemoryHistory.Last();
                ImGui.Text($"Current: {current:F1} MB | Min: {min:F1} MB | Max: {max:F1} MB");
                var deviceArray = _deviceMemoryHistory.ToArray();
                if (deviceArray.Length > 0)
                {
                    ImGui.PlotLines("##DeviceMemoryGraph", ref deviceArray[0], _deviceMemoryHistory.Count, 0,
                        null, min, max, new Vector2(ImGui.GetContentRegionAvail().X, 40));
                }
            }
        }

        private void DrawResourceBreakdown()
        {
            if (ImGui.TreeNode($"Resource Breakdown ({_currentStats.ByResourceType.Count} types)##ResourceBreakdown"))
            {
                float totalMemory = _currentStats.TotalMemory;

                if (ImGui.BeginTable("ResourceTable", 8,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable))
                {
                    ImGui.TableSetupColumn("Resource Type", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 80);
                    ImGui.TableSetupColumn("Host", ImGuiTableColumnFlags.WidthFixed, 80);
                    ImGui.TableSetupColumn("Device", ImGuiTableColumnFlags.WidthFixed, 80);
                    ImGui.TableSetupColumn("% Total", ImGuiTableColumnFlags.WidthFixed, 70);
                    ImGui.TableSetupColumn("H Allocs", ImGuiTableColumnFlags.WidthFixed, 60);
                    ImGui.TableSetupColumn("D Allocs", ImGuiTableColumnFlags.WidthFixed, 60);
                    ImGui.TableSetupColumn("Graph", ImGuiTableColumnFlags.WidthFixed, 100);
                    ImGui.TableHeadersRow();

                    foreach (var stat in _currentStats.ByResourceType)
                    {
                        if (stat.CurrentTotal == 0) continue;

                        ImGui.TableNextRow();

                        // Type name
                        ImGui.TableNextColumn();
                        ImGui.Text(stat.TypeName);

                        // Total size
                        ImGui.TableNextColumn();
                        ImGui.Text(FormatSize(stat.CurrentTotal));

                        // Host size
                        ImGui.TableNextColumn();
                        ImGui.Text(FormatSize(stat.CurrentHost));

                        // Device size
                        ImGui.TableNextColumn();
                        ImGui.Text(FormatSize(stat.CurrentDevice));

                        // Percentage
                        ImGui.TableNextColumn();
                        float percentage = totalMemory > 0 ? (float)stat.CurrentTotal / totalMemory : 0;
                        ImGui.Text($"{percentage:P1}");

                        // Host allocations
                        ImGui.TableNextColumn();
                        ImGui.Text($"{stat.HostAllocationCount}/{stat.HostFreeCount}");

                        // Device allocations
                        ImGui.TableNextColumn();
                        ImGui.Text($"{stat.DeviceAllocationCount}/{stat.DeviceFreeCount}");

                        // Visual bar
                        ImGui.TableNextColumn();
                        DrawCombinedProgressBar(
                            totalMemory > 0 ? (float)stat.CurrentHost / totalMemory : 0,
                            totalMemory > 0 ? (float)stat.CurrentDevice / totalMemory : 0,
                            new Vector2(100, 15)
                        );
                    }

                    ImGui.EndTable();
                }

                // Memory type distribution
                ImGui.Separator();
                ImGui.Text("Memory Type Distribution:");
                DrawMemoryTypeDistribution();

                ImGui.TreePop();
            }
        }

        private void DrawCombinedProgressBar(float hostPercentage, float devicePercentage, Vector2 size)
        {
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();

            // Background
            drawList.AddRectFilled(pos, new Vector2(pos.X + size.X, pos.Y + size.Y),
                ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1.0f)));

            // Device memory (VRAM) - green
            float deviceWidth = size.X * devicePercentage;
            drawList.AddRectFilled(pos, new Vector2(pos.X + deviceWidth, pos.Y + size.Y),
                ImGui.GetColorU32(new Vector4(0.0f, 0.8f, 0.4f, 1.0f)));

            // Host memory - blue
            float hostWidth = size.X * hostPercentage;
            drawList.AddRectFilled(new Vector2(pos.X + deviceWidth, pos.Y),
                new Vector2(pos.X + deviceWidth + hostWidth, pos.Y + size.Y),
                ImGui.GetColorU32(new Vector4(0.2f, 0.6f, 1.0f, 1.0f)));

            // Border
            drawList.AddRect(pos, new Vector2(pos.X + size.X, pos.Y + size.Y),
                ImGui.GetColorU32(new Vector4(0, 0, 0, 1)));

            ImGui.Dummy(size);
        }

        private void DrawMemoryTypeDistribution()
        {
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            float radius = 40;
            var center = new Vector2(pos.X + radius, pos.Y + radius);

            float total = _currentStats.TotalMemory;
            if (total > 0)
            {
                float hostPercentage = (float)_currentStats.TotalHost / total;
                float devicePercentage = (float)_currentStats.TotalDevice / total;

                // Device memory slice
                if (devicePercentage > 0)
                {
                    drawList.PathArcTo(center, radius, 0, devicePercentage * 2 * MathF.PI, 32);
                    drawList.PathLineTo(center);
                    drawList.PathFillConvex(ImGui.GetColorU32(new Vector4(0.0f, 0.8f, 0.4f, 1.0f)));
                }

                // Host memory slice
                if (hostPercentage > 0)
                {
                    drawList.PathArcTo(center, radius, devicePercentage * 2 * MathF.PI, 2 * MathF.PI, 32);
                    drawList.PathLineTo(center);
                    drawList.PathFillConvex(ImGui.GetColorU32(new Vector4(0.2f, 0.6f, 1.0f, 1.0f)));
                }

                // Border
                drawList.AddCircle(center, radius, ImGui.GetColorU32(new Vector4(1, 1, 1, 1)));
            }

            ImGui.Dummy(new Vector2(radius * 2, radius * 2));

            // Legend
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.ColorButton("##DeviceColor", new Vector4(0.0f, 0.8f, 0.4f, 1.0f));
            ImGui.SameLine();
            ImGui.Text($"Device: {(_currentStats.TotalDevice / (float)total * 100):F1}%");

            ImGui.ColorButton("##HostColor", new Vector4(0.2f, 0.6f, 1.0f, 1.0f));
            ImGui.SameLine();
            ImGui.Text($"Host: {(_currentStats.TotalHost / (float)total * 100):F1}%");
            ImGui.EndGroup();
        }

        private string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            double size = bytes;

            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }

            return $"{size:0.##} {suffixes[suffixIndex]}";
        }

        [GeneratedRegex(@"^/(?:[^/]+\/)*[^/]+$", RegexOptions.Compiled)]
        private static partial Regex UnixPathRegex();
        [GeneratedRegex(@"^[a-zA-Z]:\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\r\n]*$", RegexOptions.Compiled)]
        private static partial Regex WindowsPathRegex();
        [GeneratedRegex(@"\s+:\s*line\s+(\d+)\s*$", RegexOptions.Compiled)]
        private static partial Regex SimpleFileLineRegex();
        [GeneratedRegex(@"at\s+([^\s]+)\s+in\s+(.*):line\s+(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled, "ru-RU")]
        private static partial Regex StackFrameRegex();
    }
}