using ImGuiNET;

using RockEngine.Core.Assets;
using RockEngine.Core.Rendering;
using RockEngine.Editor.EditorUI.ImGuiRendering;
using RockEngine.Vulkan;

using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Editor.Layers
{
    public class AssetBrowserLayer : ILayer
    {
        private readonly AssetManager _assetManager;
        private readonly ImGuiController _imGuiController;
        private DirectoryInfo _currentDirectory;
        private string _selectedAssetPath;
        private string _searchQuery = string.Empty;
        private bool showFileExtensions;
        private readonly Dictionary<string, Vector4> _folderColors = new Dictionary<string, Vector4>();
        private const string FolderIcon = "\uf07b";  // FontAwesome folder icon
        private const string FileIcon = "\uf15b";    // FontAwesome file icon
        private string? _currentFolderForColorEdit;
        private bool _requestOpenColorPicker;
        private const string FOLDER_COLOR_PREFIX = "FolderColor_";

        public AssetBrowserLayer(AssetManager assetManager, ImGuiController imGuiController)
        {
            _assetManager = assetManager;
            _imGuiController = imGuiController;
        }

        public void OnDetach() { }

        public void OnImGuiRender(VkCommandBuffer vkCommandBuffer)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
            ImGui.Begin("Asset Browser", ImGuiWindowFlags.MenuBar);
            ImGui.PopStyleVar();

            DrawMenuBar();

            // Search bar
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.InputTextWithHint("##SearchAssets", "Search assets...", ref _searchQuery, 100))
            {
                // Filtering happens when search query changes
            }

            // Directory tree and asset list
            float contentHeight = ImGui.GetContentRegionAvail().Y - ImGui.GetTextLineHeightWithSpacing();
            ImGui.BeginChild("##AssetBrowserContent", new Vector2(0, contentHeight), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

            // Directory tree on left
            float treeWidth = ImGui.GetContentRegionAvail().X * 0.3f;
            ImGui.BeginChild("##DirectoryTree", new Vector2(treeWidth, 0));
            DrawDirectoryTree(new DirectoryInfo(_assetManager.BasePath));
            ImGui.EndChild();

            ImGui.SameLine();

            // Asset list on right
            ImGui.BeginChild("##AssetList");
            DrawCurrentDirectoryContents();
            ImGui.EndChild();

            ImGui.EndChild();

            // Draw the color picker popup outside of any child windows
            DrawColorPickerPopup();

            ImGui.End();
        }

        private void DrawMenuBar()
        {
            if (ImGui.BeginMenuBar())
            {
                if (ImGui.BeginMenu("Create"))
                {
                    if (ImGui.MenuItem("New Folder"))
                    {
                        CreateNewFolder();
                    }

                    if (ImGui.BeginMenu("New Asset"))
                    {
                        if (ImGui.MenuItem("Material"))
                        {
                            // Create new material
                        }

                        if (ImGui.MenuItem("Texture"))
                        {
                            // Create new texture
                        }
                        ImGui.EndMenu();

                    }
                    ImGui.EndMenu();

                }

                if (ImGui.BeginMenu("View"))
                {
                    ImGui.MenuItem("Show File Extensions", "", ref showFileExtensions);
                    ImGui.EndMenu();
                }

            }
            ImGui.EndMenuBar();
        }

        private void CreateNewFolder()
        {
            int folderNumber = 1;
            string newFolderName = "New Folder";
            string fullPath = Path.Combine(_currentDirectory.FullName, newFolderName);

            while (Directory.Exists(fullPath))
            {
                folderNumber++;
                newFolderName = $"New Folder ({folderNumber})";
                fullPath = Path.Combine(_currentDirectory.FullName, newFolderName);
            }

            Directory.CreateDirectory(fullPath);
        }

        private void DrawDirectoryTree(DirectoryInfo dir)
        {
            if (!dir.Exists) return;

            ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanFullWidth;
            if (dir.FullName == _currentDirectory.FullName)
                flags |= ImGuiTreeNodeFlags.Selected;

            // Apply custom folder color if available
            if (_folderColors.TryGetValue(dir.Name, out Vector4 color))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, color);
            }

            bool isOpen = ImGui.TreeNodeEx($"{FolderIcon} {dir.Name}", flags);

            // Reset color if we pushed it
            if (_folderColors.ContainsKey(dir.Name))
            {
                ImGui.PopStyleColor();
            }

            if (ImGui.IsItemClicked())
            {
                _currentDirectory = dir;
            }

            // Folder context menu
            if (ImGui.BeginPopupContextItem(dir.FullName))
            {
                if (ImGui.MenuItem("Change Color"))
                {
                    _currentFolderForColorEdit = dir.Name;
                    _requestOpenColorPicker = true;  // Set flag instead of direct OpenPopup
                }

                if (ImGui.MenuItem("Rename"))
                {
                    // Handle rename
                }

                if (ImGui.MenuItem("Delete"))
                {
                    // Handle delete
                }

                ImGui.EndPopup();
            }

            if (isOpen)
            {
                foreach (var subDir in dir.GetDirectories())
                {
                    DrawDirectoryTree(subDir);
                }
                ImGui.TreePop();
            }
        }

        private void DrawColorPickerPopup()
        {
            if (_requestOpenColorPicker)
            {
                ImGui.OpenPopup("##FolderColorPicker");
                _requestOpenColorPicker = false;
            }

            // Draw the popup
            if (ImGui.BeginPopup("##FolderColorPicker"))
            {
                if (_currentFolderForColorEdit != null)
                {
                    Vector4 color = _folderColors.TryGetValue(_currentFolderForColorEdit, out Vector4 existingColor)
                        ? existingColor
                        : new Vector4(1, 1, 1, 1);

                    if (ImGui.ColorPicker4("##FolderColor", ref color, ImGuiColorEditFlags.NoAlpha))
                    {
                        _folderColors[_currentFolderForColorEdit] = color;
                    }

                    if (ImGui.Button("Apply"))
                    {
                        ImGui.CloseCurrentPopup();
                        _currentFolderForColorEdit = null;
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Cancel"))
                    {
                        ImGui.CloseCurrentPopup();
                        _currentFolderForColorEdit = null;
                    }
                }
                ImGui.EndPopup();
            }
        }

        private void DrawCurrentDirectoryContents()
        {
            // Header with current path and buttons
            ImGui.Text(_currentDirectory.FullName);
            ImGui.SameLine();

            if (ImGui.Button("Create Folder"))
            {
                CreateNewFolder();
            }

            ImGui.Separator();

            // Parent directory button
            if (_currentDirectory.Parent != null)
            {
                if (ImGui.Button($"{FolderIcon} .."))
                {
                    _currentDirectory = _currentDirectory.Parent;
                }
                ImGui.Separator();
            }

            // Draw folders first
            foreach (var dir in _currentDirectory.GetDirectories())
            {
                DrawFolderItem(dir);
            }

            // Draw files
            foreach (var file in _currentDirectory.GetFiles("*.asset"))
            {
                DrawAssetItem(file);
            }
        }

        private void DrawFolderItem(DirectoryInfo dir)
        {
            // Apply custom color if available
            bool hasColor = _folderColors.TryGetValue(dir.Name, out Vector4 color);
            if (hasColor)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, color);
            }

            bool isSelected = ImGui.Selectable($"{FolderIcon} {dir.Name}", false,
                ImGuiSelectableFlags.AllowDoubleClick);

            if (hasColor)
            {
                ImGui.PopStyleColor();
            }

            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                _currentDirectory = dir;
            }

            // Folder context menu
            if (ImGui.BeginPopupContextItem(dir.FullName))
            {
                if (ImGui.MenuItem("Change Color"))
                {
                    _currentFolderForColorEdit = dir.Name;
                    _requestOpenColorPicker = true;
                }

                ImGui.EndPopup();
            }

            // Drag and drop target for moving assets
            if (ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload("ASSET_PATH");
                if (payload.Data != default)
                {
                    string assetPath = Marshal.PtrToStringAuto(payload.Data);
                    // Move asset to this folder
                }
                ImGui.EndDragDropTarget();
            }
        }

        private void DrawAssetItem(FileInfo file)
        {
            bool isSelected = _selectedAssetPath == file.FullName;
            string fileName = Path.GetFileNameWithoutExtension(file.Name);

            // Filter by search query
            if (!string.IsNullOrEmpty(_searchQuery) &&
                !fileName.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Get icon based on asset type
            string icon = GetAssetIcon(file);

            if (ImGui.Selectable($"{icon} {fileName}", isSelected))
            {
                _selectedAssetPath = file.FullName;
            }

            // Context menu for assets
            if (ImGui.BeginPopupContextItem(file.FullName))
            {
                if (ImGui.MenuItem("Rename"))
                {
                    // Handle rename
                }

                if (ImGui.MenuItem("Delete"))
                {
                    // Handle delete
                }

                ImGui.EndPopup();
            }

            // Preview on hover
            if (ImGui.IsItemHovered())
            {
                DrawAssetTooltip(file);
            }
        }

        private string GetAssetIcon(FileInfo file)
        {
            try
            {
                var relativePath = Path.GetRelativePath(_assetManager.BasePath, file.FullName);
                var metadata = _assetManager.LoadMetadataAsync(new AssetPath(relativePath)).GetAwaiter().GetResult();

                return metadata?.Type switch
                {
                    "Material" => "\uf1fc",   // FontAwesome material icon
                    "Texture" => "\uf03e",    // FontAwesome image icon
                    "Mesh" => "\uf1b2",       // FontAwesome cube icon
                    _ => FileIcon
                };
            }
            catch
            {
                return FileIcon;
             }
            }

        private void DrawAssetTooltip(FileInfo file)
        {
            ImGui.BeginTooltip();

            string relativePath = Path.GetRelativePath(_assetManager.BasePath, file.FullName);
            var metadata = _assetManager.LoadMetadataAsync(new AssetPath(relativePath)).GetAwaiter().GetResult();

            if (metadata != null)
            {
                ImGui.Text(metadata.Name);
                ImGui.Separator();
                //ImGui.Text($"Type: {metadata.Type}");
                ImGui.Text($"Path: {relativePath}");
                ImGui.Text($"Created: {metadata.Created:g}");
                ImGui.Text($"Modified: {metadata.Modified:g}");
                ImGui.Text($"Size: {file.Length / 1024} KB");
            }
            else
            {
                ImGui.Text(Path.GetFileNameWithoutExtension(file.Name));
                ImGui.Text("Unknown asset type");
            }

            ImGui.EndTooltip();
        }

        public void OnRender(VkCommandBuffer vkCommandBuffer) { }
        public void OnUpdate() { }

        public async Task OnAttach()
        {
            _currentDirectory = new DirectoryInfo(_assetManager.BasePath);

            // Initialize some default folder colors
            _folderColors["Materials"] = new Vector4(0.4f, 0.7f, 1.0f, 1.0f);  // Blue
            _folderColors["Textures"] = new Vector4(0.9f, 0.6f, 0.2f, 1.0f);    // Orange
            _folderColors["Models"] = new Vector4(0.3f, 0.8f, 0.4f, 1.0f);      // Green
            _folderColors["Scenes"] = new Vector4(0.8f, 0.4f, 0.9f, 1.0f);       // Purple
        }

        

    }
}