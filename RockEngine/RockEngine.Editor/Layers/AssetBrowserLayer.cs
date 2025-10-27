using ImGuiNET;

using NLog;

using RockEngine.Core.Assets;
using RockEngine.Core.Assets.RockEngine.Core.Assets;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Editor.EditorUI.ImGuiRendering;
using RockEngine.Vulkan;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;

namespace RockEngine.Editor.Layers
{
    public class AssetBrowserLayer : ILayer
    {
        private readonly AssetManager _assetManager;
        private readonly ImGuiController _imGuiController;
        private string _currentDirectoryPath;
        private string _searchQuery = string.Empty;
        private bool showFileExtensions;
        private readonly Dictionary<string, Vector4> _folderColors = new Dictionary<string, Vector4>();
        private const string FolderIcon = "\uf07b";
        private const string FileIcon = "\uf15b";
        private bool _gridView = true;
        private float _thumbnailSize = 64f;
        private HashSet<string> _selectedAssets = new HashSet<string>();
        private bool _isLoadingScene = false;
        private string _loadingSceneName = "";

        // State management
        private class RenameState
        {
            public string ItemPath;
            public string NewName = string.Empty;
            public bool IsActive;
        }

        private class DeleteState
        {
            public string ItemPath;
            public string ItemName;
            public bool ShowConfirmation;
        }

        private class ColorPickerState
        {
            public string FolderName;
            public bool RequestOpen;
        }

        private readonly RenameState _renameState = new RenameState();
        private readonly DeleteState _deleteState = new DeleteState();
        private readonly ColorPickerState _colorPickerState = new ColorPickerState();

        // Cache for asset metadata
        private readonly ConcurrentDictionary<string, AssetMetadataInfo> _metadataCache = new ConcurrentDictionary<string, AssetMetadataInfo>();
        private readonly ConcurrentDictionary<Guid, AssetMetadataInfo> _metadataByIdCache = new ConcurrentDictionary<Guid, AssetMetadataInfo>();
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        // Drag and drop
        private string _dragHoverFolderPath;
        private Stopwatch _dragHoverTimer = new Stopwatch();
        private const double HOVER_OPEN_DELAY = 1000; // 1 second delay

        public AssetBrowserLayer(AssetManager assetManager, ImGuiController imGuiController)
        {
            _assetManager = assetManager;
            _imGuiController = imGuiController;
        }

        public Task OnImGuiRender(VkCommandBuffer vkCommandBuffer)
        {
            DrawMainWindow();
            return Task.CompletedTask;
        }

        private void DrawMainWindow()
        {
            // Reset hover timer if not dragging
            if (!ImGui.IsMouseDragging(ImGuiMouseButton.Left) && !AssetDragDrop.IsAnyPayloadActive())
            {
                _dragHoverTimer.Stop();
                _dragHoverFolderPath = null;
            }

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
            ImGui.Begin("Asset Browser", ImGuiWindowFlags.MenuBar);
            ImGui.PopStyleVar();

            DrawMenuBar();
            DrawControls();
            DrawContentArea();

            DrawColorPickerPopup();
            DrawLoadingPopup();
            DrawRenamePopup();
            DrawDeleteConfirmationPopup();

            ImGui.End();
        }

        private void DrawControls()
        {
            ImGui.BeginChild("##AssetBrowserControls", new Vector2(0, ImGui.GetFrameHeightWithSpacing() * 2));

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.7f);
            ImGui.InputTextWithHint("##SearchAssets", "Search assets...", ref _searchQuery, 100);

            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.3f);
            ImGui.SliderFloat("##ThumbnailSize", ref _thumbnailSize, 32f, 128f, "Size: %.0f");

            ImGui.SameLine();
            if (ImGui.Button(_gridView ? "\uf00a" : "\uf03a"))
            {
                _gridView = !_gridView;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(_gridView ? "Switch to List View" : "Switch to Grid View");
            }

            ImGui.EndChild();
        }

        private void DrawContentArea()
        {
            float contentHeight = ImGui.GetContentRegionAvail().Y;
            ImGui.BeginChild("##AssetBrowserContent", new Vector2(0, contentHeight), true, ImGuiWindowFlags.HorizontalScrollbar);

            // Directory tree on left
            float treeWidth = ImGui.GetContentRegionAvail().X * 0.25f;
            ImGui.BeginChild("##DirectoryTree", new Vector2(treeWidth, 0), true);
            DrawDirectoryTree(_assetManager.BasePath);
            ImGui.EndChild();

            ImGui.SameLine();

            // Asset list on right
            ImGui.BeginChild("##AssetList", new Vector2(0, 0), true);
            DrawCurrentDirectoryContents();
            ImGui.EndChild();

            ImGui.EndChild();
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
                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("View"))
                {
                    ImGui.MenuItem("Show File Extensions", "", ref showFileExtensions);
                    ImGui.MenuItem("Grid View", "", ref _gridView);
                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Selection"))
                {
                    if (ImGui.MenuItem("Select All", "Ctrl+A"))
                    {
                        SelectAllAssets();
                    }

                    if (ImGui.MenuItem("Deselect All", "Ctrl+D"))
                    {
                        DeselectAllAssets();
                    }

                    ImGui.EndMenu();
                }
            }
            ImGui.EndMenuBar();
        }

        private void SelectAllAssets()
        {
            _selectedAssets.Clear();
            try
            {
                var files = Directory.GetFiles(_currentDirectoryPath, "*.asset");
                foreach (var file in files)
                {
                    _selectedAssets.Add(file);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to select all assets");
            }
        }

        private void DeselectAllAssets()
        {
            _selectedAssets.Clear();
        }

        private void CreateNewFolder()
        {
            try
            {
                int folderNumber = 1;
                string newFolderName = "New Folder";
                string fullPath = Path.Combine(_currentDirectoryPath, newFolderName);

                while (Directory.Exists(fullPath))
                {
                    folderNumber++;
                    newFolderName = $"New Folder ({folderNumber})";
                    fullPath = Path.Combine(_currentDirectoryPath, newFolderName);
                }

                Directory.CreateDirectory(fullPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to create new folder");
            }
        }

        private void CreateNewAsset<T>(string defaultFolder, string defaultName) where T : IAsset
        {
            // Implementation for creating new assets
        }

        private void DrawDirectoryTree(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            try
            {
                var dir = new DirectoryInfo(path);
                string dirName = dir.Name;

                ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanFullWidth;
                if (path == _currentDirectoryPath)
                {
                    flags |= ImGuiTreeNodeFlags.Selected;
                }

                // Apply custom folder color if available
                if (_folderColors.TryGetValue(dirName, out Vector4 color))
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, color);
                }

                bool isOpen = ImGui.TreeNodeEx($"{FolderIcon} {dirName}", flags);

                // Reset color if we pushed it
                if (_folderColors.ContainsKey(dirName))
                {
                    ImGui.PopStyleColor();
                }

                if (ImGui.IsItemClicked())
                {
                    _currentDirectoryPath = path;
                    _selectedAssets.Clear();
                }

                // Handle drag hover
                if (ImGui.IsItemHovered() && (ImGui.IsMouseDragging(ImGuiMouseButton.Left) || AssetDragDrop.IsAnyPayloadActive()))
                {
                    HandleDragHover(path);
                }

                // Make the tree node a drop target
                if (ImGui.BeginDragDropTarget())
                {
                    if (AssetDragDrop.AcceptAssetDrop(out Guid assetId))
                    {
                        MoveAssetToDirectory(assetId, path);
                    }
                    else if (AssetDragDrop.AcceptFolderDrop(out string sourcePath))
                    {
                        MoveItem(sourcePath, path);
                    }
                    ImGui.EndDragDropTarget();
                }

                DrawFolderContextMenu(path, dirName);

                if (isOpen)
                {
                    foreach (var subDir in Directory.GetDirectories(path).OrderBy(d => d))
                    {
                        DrawDirectoryTree(subDir);
                    }
                    ImGui.TreePop();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to draw directory tree for path: {path}");
            }
        }

        private void DrawFolderContextMenu(string path, string dirName)
        {
            if (ImGui.BeginPopupContextItem(path))
            {
                if (ImGui.MenuItem("Change Color"))
                {
                    _colorPickerState.FolderName = dirName;
                    _colorPickerState.RequestOpen = true;
                }

                if (ImGui.MenuItem("Rename"))
                {
                    _renameState.ItemPath = path;
                    _renameState.NewName = dirName;
                    _renameState.IsActive = true;
                }

                if (ImGui.MenuItem("Delete"))
                {
                    _deleteState.ItemPath = path;
                    _deleteState.ItemName = dirName;
                    _deleteState.ShowConfirmation = true;
                }

                ImGui.EndPopup();
            }
        }

        private void DrawColorPickerPopup()
        {
            if (_colorPickerState.RequestOpen)
            {
                ImGui.OpenPopup("##FolderColorPicker");
                _colorPickerState.RequestOpen = false;
            }

            if (ImGui.BeginPopup("##FolderColorPicker"))
            {
                if (_colorPickerState.FolderName != null)
                {
                    Vector4 color = _folderColors.TryGetValue(_colorPickerState.FolderName, out Vector4 existingColor)
                        ? existingColor
                        : new Vector4(1, 1, 1, 1);

                    if (ImGui.ColorPicker4("##FolderColor", ref color, ImGuiColorEditFlags.NoAlpha))
                    {
                        _folderColors[_colorPickerState.FolderName] = color;
                    }

                    if (ImGui.Button("Apply"))
                    {
                        ImGui.CloseCurrentPopup();
                        _colorPickerState.FolderName = null;
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Cancel"))
                    {
                        ImGui.CloseCurrentPopup();
                        _colorPickerState.FolderName = null;
                    }
                }
                ImGui.EndPopup();
            }
        }

        private void DrawLoadingPopup()
        {
            if (_isLoadingScene)
            {
                ImGui.OpenPopup("Loading Scene");

                Vector2 center = ImGui.GetMainViewport().GetCenter();
                ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
                ImGui.SetNextWindowContentSize(new Vector2(200, 100));

                if (ImGui.BeginPopupModal("Loading Scene", ref _isLoadingScene, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
                {
                    ImGui.Text($"Loading scene: {_loadingSceneName}");

                    // Center the spinner using available space
                    float spinnerRadius = 20f;
                    float spinnerThickness = 4f;
                    uint spinnerColor = ImGui.GetColorU32(ImGuiCol.ButtonHovered);

                    // Calculate center position within the popup
                    Vector2 windowSize = ImGui.GetWindowSize();
                    Vector2 windowPos = ImGui.GetWindowPos();
                    Vector2 centerPos = new Vector2(
                        windowPos.X + windowSize.X * 0.5f,
                        windowPos.Y + windowSize.Y * 0.5f + ImGui.GetTextLineHeight() // Offset below text
                    );

                    // Draw the spinner at the calculated center position
                    ImGuiSpinnerExtension.Spinner(centerPos, spinnerRadius, spinnerThickness, spinnerColor);

                    ImGui.EndPopup();
                }
            }
        }

        private void DrawCurrentDirectoryContents()
        {
            if (string.IsNullOrEmpty(_currentDirectoryPath) || !Directory.Exists(_currentDirectoryPath))
            {
                ImGui.Text("Invalid directory");
                return;
            }

            try
            {
                ImGui.Text(_currentDirectoryPath);
                ImGui.SameLine();

                if (ImGui.Button("Create Folder"))
                {
                    CreateNewFolder();
                }

                ImGui.Separator();

                // Parent directory button
                var parentDir = Directory.GetParent(_currentDirectoryPath);
                if (parentDir != null && parentDir.Exists)
                {
                    if (ImGui.Button($"{FolderIcon} .."))
                    {
                        _currentDirectoryPath = parentDir.FullName;
                        _selectedAssets.Clear();
                    }
                    ImGui.Separator();
                }

                if (_gridView)
                {
                    DrawAssetGrid();
                }
                else
                {
                    DrawAssetList();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to draw directory contents for path: {_currentDirectoryPath}");
                ImGui.Text("Error loading directory contents");
            }
        }

        private void DrawAssetGrid()
        {
            float availableWidth = ImGui.GetContentRegionAvail().X;
            int columns = Math.Max(1, (int)(availableWidth / (_thumbnailSize + ImGui.GetStyle().ItemSpacing.X)));

            if (ImGui.BeginTable("AssetGrid", columns, ImGuiTableFlags.SizingFixedFit))
            {
                // Draw folders
                foreach (var dir in Directory.GetDirectories(_currentDirectoryPath).OrderBy(d => d))
                {
                    ImGui.TableNextColumn();
                    DrawFolderGridItem(new DirectoryInfo(dir));
                }

                // Draw files
                foreach (var file in Directory.GetFiles(_currentDirectoryPath, "*.asset").OrderBy(f => f))
                {
                    ImGui.TableNextColumn();
                    DrawAssetGridItem(new FileInfo(file));
                }

                ImGui.EndTable();
            }
        }

        private void DrawAssetList()
        {
            // Draw folders first in list view
            foreach (var dir in Directory.GetDirectories(_currentDirectoryPath).OrderBy(d => d))
            {
                DrawFolderListItem(new DirectoryInfo(dir));
            }

            // Then draw files in list view
            foreach (var file in Directory.GetFiles(_currentDirectoryPath, "*.asset").OrderBy(f => f))
            {
                DrawAssetListItem(new FileInfo(file));
            }
        }

        private void DrawFolderGridItem(DirectoryInfo dir)
        {
            ImGui.PushID(dir.FullName);
            ImGui.BeginGroup();

            bool hasColor = _folderColors.TryGetValue(dir.Name, out Vector4 color);
            if (hasColor)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, color);
            }

            if (ImGui.Button($"{FolderIcon}##{dir.Name}", new Vector2(_thumbnailSize, _thumbnailSize)))
            {
                _currentDirectoryPath = dir.FullName;
                _selectedAssets.Clear();
            }

            if (hasColor)
            {
                ImGui.PopStyleColor();
            }

            string folderName = dir.Name;
            if (folderName.Length > 12)
            {
                folderName = string.Concat(folderName.AsSpan(0, 10), "...");
            }

            ImGui.TextWrapped(folderName);
            ImGui.EndGroup();

            // Use AssetDragDrop for drag source
            AssetDragDrop.BeginDragDropSource(dir.FullName, dir.Name);

            // Use AssetDragDrop for drop target
            HandleDropTarget(dir.FullName);

            DrawFolderContextMenu(dir.FullName, dir.Name);
            ImGui.PopID();
        }

        private void DrawFolderListItem(DirectoryInfo dir)
        {
            ImGui.PushID(dir.FullName);

            bool hasColor = _folderColors.TryGetValue(dir.Name, out Vector4 color);
            if (hasColor)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, color);
            }

            bool isSelected = ImGui.Selectable($"{FolderIcon} {dir.Name}", false,
                ImGuiSelectableFlags.AllowDoubleClick | ImGuiSelectableFlags.SpanAllColumns);

            if (hasColor)
            {
                ImGui.PopStyleColor();
            }

            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                _currentDirectoryPath = dir.FullName;
                _selectedAssets.Clear();
            }

            // Use AssetDragDrop for drag source
            AssetDragDrop.BeginDragDropSource(dir.FullName, dir.Name);

            // Use AssetDragDrop for drop target
            HandleDropTarget(dir.FullName);

            DrawFolderContextMenu(dir.FullName, dir.Name);
            ImGui.PopID();
        }

        private void DrawAssetGridItem(FileInfo file)
        {
            ImGui.PushID(file.FullName);

            string relativePath = Path.GetRelativePath(_assetManager.BasePath, file.FullName);
            string fileName = showFileExtensions ? file.Name : Path.GetFileNameWithoutExtension(file.Name);

            if (!string.IsNullOrEmpty(_searchQuery) && !fileName.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
            {
                ImGui.PopID();
                return;
            }

            string icon = GetAssetIcon(relativePath);
            var metadata = GetCachedMetadata(relativePath);

            bool isSelected = _selectedAssets.Contains(file.FullName);

            ImGui.BeginGroup();

            if (isSelected)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.Header]);
            }

            if (ImGui.Button($"{icon}##{file.Name}", new Vector2(_thumbnailSize, _thumbnailSize)))
            {
                HandleAssetSelection(file.FullName);

                // Set as selected asset for properties window
                if (metadata != null)
                {
                    var asset = _assetManager.GetAsset<IAsset>(metadata.ID);
                    AssetSelection.SelectedAsset = asset;
                }

                // Open scene on single click in grid view
                if (metadata != null && metadata.Type == "SceneAsset")
                {
                    _ = Task.Factory.StartNew(() => OpenSceneAsset(metadata.ID, metadata.Name), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Current);
                }
            }

            if (isSelected)
            {
                ImGui.PopStyleColor();
            }

            if (metadata != null)
            {
                AssetDragDrop.BeginDragDropSource(metadata.ID, fileName);
            }

            string displayName = fileName;
            if (displayName.Length > 12)
            {
                displayName = string.Concat(displayName.AsSpan(0, 10), "...");
            }

            ImGui.TextWrapped(displayName);
            ImGui.EndGroup();

            if (ImGui.IsItemHovered())
            {
                DrawAssetTooltip(relativePath, file);
            }

            DrawAssetContextMenu(file);
            ImGui.PopID();
        }

        private Type GetAssetTypeFromMetadata(AssetMetadataInfo metadata)
        {
            return metadata.Type switch
            {
                "TextureAsset" => typeof(TextureAsset),
                "MaterialAsset" => typeof(MaterialAsset),
                "MeshAsset" => typeof(MeshAsset),
                "SceneAsset" => typeof(SceneAsset),
                _ => typeof(IAsset)
            };
        }

        private void DrawAssetListItem(FileInfo file)
        {
            ImGui.PushID(file.FullName);

            string relativePath = Path.GetRelativePath(_assetManager.BasePath, file.FullName);
            string fileName = showFileExtensions ? file.Name : Path.GetFileNameWithoutExtension(file.Name);

            if (!string.IsNullOrEmpty(_searchQuery) && !fileName.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
            {
                ImGui.PopID();
                return;
            }

            string icon = GetAssetIcon(relativePath);
            var metadata = GetCachedMetadata(relativePath);

            bool isSelected = _selectedAssets.Contains(file.FullName);

            if (ImGui.Selectable($"{icon} {fileName}", isSelected,
                ImGuiSelectableFlags.AllowDoubleClick | ImGuiSelectableFlags.SpanAllColumns))
            {
                HandleAssetSelection(file.FullName);

                // Open scene on double click in list view
                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) &&
                    metadata != null && metadata.Type == "SceneAsset")
                {
                    _ = Task.Factory.StartNew(() => OpenSceneAsset(metadata.ID, metadata.Name), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Current);
                }
            }

            // Use AssetDragDrop for drag source if we have metadata
            if (metadata != null)
            {
                AssetDragDrop.BeginDragDropSource(metadata.ID, fileName);
            }

            // Use AssetDragDrop for drop target
            HandleDropTarget(_currentDirectoryPath);

            if (ImGui.IsItemHovered())
            {
                DrawAssetTooltip(relativePath, file);
            }

            DrawAssetContextMenu(file);
            ImGui.PopID();
        }

        private void HandleDropTarget(string targetPath)
        {
            if (AssetDragDrop.AcceptAssetDrop(out Guid assetId))
            {
                MoveAssetToDirectory(assetId, targetPath);
            }
            else if (AssetDragDrop.AcceptFolderDrop(out string sourcePath))
            {
                MoveItem(sourcePath, targetPath);
            }
        }

        private void HandleDragHover(string folderPath)
        {
            if (_dragHoverFolderPath != folderPath)
            {
                // New folder being hovered, start timer
                _dragHoverFolderPath = folderPath;
                _dragHoverTimer.Restart();
            }
            else if (_dragHoverTimer.IsRunning && _dragHoverTimer.ElapsedMilliseconds > HOVER_OPEN_DELAY)
            {
                // Hover time exceeded, open the folder
                _currentDirectoryPath = folderPath;
                _dragHoverTimer.Stop();
                _dragHoverFolderPath = null;
            }
        }

        private void DrawAssetContextMenu(FileInfo file)
        {
            if (ImGui.BeginPopupContextItem(file.FullName))
            {
                if (ImGui.MenuItem("Rename"))
                {
                    _renameState.ItemPath = file.FullName;
                    _renameState.NewName = Path.GetFileNameWithoutExtension(file.Name);
                    _renameState.IsActive = true;
                }

                if (ImGui.MenuItem("Delete"))
                {
                    _deleteState.ItemPath = file.FullName;
                    _deleteState.ItemName = Path.GetFileNameWithoutExtension(file.Name);
                    _deleteState.ShowConfirmation = true;
                }

                ImGui.EndPopup();
            }
        }

        private void DrawRenamePopup()
        {
            if (!_renameState.IsActive)
            {
                return;
            }

            ImGui.OpenPopup("Rename Item");

            Vector2 center = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

            if (ImGui.BeginPopupModal("Rename Item", ref _renameState.IsActive, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("New name:");
                ImGui.InputText("##NewName", ref _renameState.NewName, 255);

                if (ImGui.Button("OK"))
                {
                    RenameItem(_renameState.ItemPath, _renameState.NewName);
                    _renameState.IsActive = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel"))
                {
                    _renameState.IsActive = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
        }

        private void DrawDeleteConfirmationPopup()
        {
            if (!_deleteState.ShowConfirmation)
            {
                return;
            }

            ImGui.OpenPopup("Confirm Delete");

            Vector2 center = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

            if (ImGui.BeginPopupModal("Confirm Delete", ref _deleteState.ShowConfirmation, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text($"Are you sure you want to delete '{_deleteState.ItemName}'?");
                ImGui.Text("This action cannot be undone.");

                if (ImGui.Button("Yes"))
                {
                    DeleteItem(_deleteState.ItemPath);
                    _deleteState.ShowConfirmation = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button("No"))
                {
                    _deleteState.ShowConfirmation = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
        }

        private void RenameItem(string itemPath, string newName)
        {
            try
            {
                string directory = Path.GetDirectoryName(itemPath);
                string newPath = Path.Combine(directory, newName);

                if (File.Exists(itemPath))
                {
                    File.Move(itemPath, newPath);

                    // Update asset metadata if it's an asset file
                    if (itemPath.EndsWith(".asset"))
                    {
                        string oldRelativePath = Path.GetRelativePath(_assetManager.BasePath, itemPath);
                        string newRelativePath = Path.GetRelativePath(_assetManager.BasePath, newPath);

                        // Refresh metadata cache
                        _metadataCache.TryRemove(oldRelativePath, out _);
                        _assetManager.GetMetadataByPath(newRelativePath);
                    }
                }
                else if (Directory.Exists(itemPath))
                {
                    Directory.Move(itemPath, newPath);
                }

                _logger.Info($"Renamed {itemPath} to {newPath}");
                RefreshMetadataCache();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to rename {itemPath} to {newName}");
            }
        }

        private void DeleteItem(string itemPath)
        {
            try
            {
                if (File.Exists(itemPath))
                {
                    File.Delete(itemPath);

                    // Remove from asset manager if it's an asset file
                    if (itemPath.EndsWith(".asset"))
                    {
                        string relativePath = Path.GetRelativePath(_assetManager.BasePath, itemPath);
                        _metadataCache.TryRemove(relativePath, out _);
                    }
                }
                else if (Directory.Exists(itemPath))
                {
                    Directory.Delete(itemPath, true);
                }

                _logger.Info($"Deleted {itemPath}");
                RefreshMetadataCache();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to delete {itemPath}");
            }
        }

        private void MoveAssetToDirectory(Guid assetId, string targetDirectory)
        {
            try
            {
                var asset = _assetManager.GetAsset<IAsset>(assetId);
                if (asset != null)
                {
                    string newPath = Path.Combine(targetDirectory, $"{asset.Name}.asset");
                    _ = _assetManager.MoveAssetAsync(assetId, newPath);
                    RefreshMetadataCache();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to move asset {assetId} to {targetDirectory}");
            }
        }

        private void HandleAssetSelection(string assetPath)
        {
            if (ImGui.GetIO().KeyCtrl)
            {
                if (!_selectedAssets.Remove(assetPath))
                {
                    _selectedAssets.Add(assetPath);
                }
            }
            else if (ImGui.GetIO().KeyShift)
            {
                if (!_gridView)
                {
                    // Implement range selection logic
                }
            }
            else
            {
                _selectedAssets.Clear();
                _selectedAssets.Add(assetPath);
            }
        }

        private void MoveItem(string sourcePath, string targetPath)
        {
            try
            {
                string fileName = Path.GetFileName(sourcePath);
                string destinationPath = Path.Combine(targetPath, fileName);

                if (File.Exists(sourcePath))
                {
                    File.Move(sourcePath, destinationPath);

                    // Update asset metadata if it's an asset file
                    if (sourcePath.EndsWith(".asset"))
                    {
                        _ = _assetManager.MoveAssetAsync(sourcePath, destinationPath);
                        return;
                    }
                }
                else if (Directory.Exists(sourcePath))
                {
                    Directory.Move(sourcePath, destinationPath);
                }

                _logger.Info($"Moved {sourcePath} to {destinationPath}");
                RefreshMetadataCache();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to move {sourcePath} to {targetPath}");
            }
        }

        private async Task OpenSceneAsset(Guid sceneAssetId, string sceneName)
        {
            _isLoadingScene = true;
            _loadingSceneName = sceneName;

            try
            {
                var sceneAsset = await _assetManager.LoadAssetByIdAsync<SceneAsset>(sceneAssetId);
                if (sceneAsset != null)
                {
                    sceneAsset.InstantiateEntities();
                    _logger.Info($"Opened scene: {sceneAsset.Name}");
                    GC.Collect();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to open scene asset: {sceneAssetId}");
            }
            finally
            {
                _isLoadingScene = false;
            }
        }

        private string GetAssetIcon(string relativePath)
        {
            try
            {
                var metadata = GetCachedMetadata(relativePath);
                if (metadata != null)
                {
                    return metadata.Type switch
                    {
                        "MaterialAsset" => "\uf1fc",
                        "TextureAsset" => "\uf03e",
                        "MeshAsset" => "\uf1b2",
                        "ModelAsset" => "\uf1b2",
                        "SceneAsset" => "\uf0c5",
                        _ => FileIcon
                    };
                }
            }
            catch
            {
                // Fall through to default icon
            }
            return FileIcon;
        }

        private void DrawAssetTooltip(string relativePath, FileInfo file)
        {
            ImGui.BeginTooltip();

            var metadata = GetCachedMetadata(relativePath);
            if (metadata != null)
            {
                ImGui.Text(metadata.Name);
                ImGui.Separator();
                ImGui.Text($"Type: {metadata.Type.Replace("Asset", "")}");
                ImGui.Text($"ID: {metadata.ID}");
                ImGui.Text($"Size: {file.Length / 1024} KB");

                // Preview for texture assets - handle cube textures differently
                if (metadata.Type == "TextureAsset")
                {
                    try
                    {
                        var textureAsset = _assetManager.GetAsset<TextureAsset>(metadata.ID);
                        if (textureAsset != null)
                        {
                            if (!textureAsset.GpuReady)
                            {
                                _ = textureAsset.LoadGpuResourcesAsync();
                            }

                            ImGui.Separator();
                            ImGui.Text("Preview:");

                            // Check if it's a cube texture
                            if (textureAsset.TextureType == TextureType.TextureCube && textureAsset.GpuReady)
                            {
                                // For cube textures, just show an icon instead of trying to render
                                ImGui.Text("\uf1b2 Cube Texture"); // Cube icon
                                ImGui.Text($"Dimensions: {(textureAsset.Texture as Texture3D).Width}x{(textureAsset.Texture as Texture3D).Height}");
                            }
                            else
                            {
                                // For 2D textures, show the actual preview
                                if (textureAsset.Texture is not null)
                                {
                                    IntPtr texId = _imGuiController.GetTextureID(textureAsset.Texture);
                                    float previewSize = 128f;
                                    ImGui.Text($"Mip Levels: {textureAsset.Texture.LoadedMipLevels}/{textureAsset.Texture.TotalMipLevels}");
                                    ImGui.Image(texId, new Vector2(previewSize, previewSize));
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore errors in preview
                    }
                }
            }
            else
            {
                ImGui.Text(Path.GetFileNameWithoutExtension(file.Name));
                ImGui.Text("Unknown asset type");
            }

            ImGui.EndTooltip();
        }

        private AssetMetadataInfo GetCachedMetadata(string relativePath)
        {
            if (_metadataCache.TryGetValue(relativePath, out var cachedMetadata))
            {
                return cachedMetadata;
            }

            var metadata = _assetManager.GetMetadataByPath(relativePath);
            if (metadata != null)
            {
                _metadataCache[relativePath] = metadata;
                return metadata;
            }

            return null;
        }

        public void RefreshMetadataCache()
        {
            _metadataCache.Clear();
            _metadataByIdCache.Clear();
        }

        public async Task<T> GetAssetWithMetadataAsync<T>(Guid assetId) where T : class, IAsset
        {
            if (_metadataByIdCache.TryGetValue(assetId, out var metadata))
            {
                var asset = _assetManager.GetAsset<T>(assetId);
                if (asset != null)
                {
                    return asset;
                }
            }

            var loadedAsset = await _assetManager.LoadAssetByIdAsync<T>(assetId);
            if (loadedAsset != null)
            {
                var assetMetadata = new AssetMetadataInfo(
                    loadedAsset.ID,
                    loadedAsset.Name,
                    loadedAsset.GetType().Name
                );
                _metadataByIdCache[assetId] = assetMetadata;
            }

            return loadedAsset;
        }

        public void OnRender(VkCommandBuffer vkCommandBuffer) { }
        public void OnUpdate() { }

        public async Task OnAttach()
        {
            _currentDirectoryPath = _assetManager.BasePath;

            _folderColors["Materials"] = new Vector4(0.4f, 0.7f, 1.0f, 1.0f);
            _folderColors["Textures"] = new Vector4(0.9f, 0.6f, 0.2f, 1.0f);
            _folderColors["Models"] = new Vector4(0.3f, 0.8f, 0.4f, 1.0f);
            _folderColors["Scenes"] = new Vector4(0.8f, 0.4f, 0.9f, 1.0f);

            _assetManager.OnAssetsChanged += RefreshMetadataCache;
            _assetManager.OnAssetRemoved += OnAssetRemoved;
            _assetManager.OnAssetRegistered += OnAssetRegistered;
        }

        public void OnDetach()
        {
            _assetManager.OnAssetsChanged -= RefreshMetadataCache;
            _assetManager.OnAssetRemoved -= OnAssetRemoved;
            _assetManager.OnAssetRegistered -= OnAssetRegistered;
        }

        private void OnAssetRemoved(Guid assetId)
        {
            _logger.Info($"Asset removed: {assetId}");
            _metadataCache.Clear();
        }

        private void OnAssetRegistered(IAsset asset)
        {
            _logger.Info($"Asset registered: {asset.Name} ({asset.ID})");
            RefreshMetadataCache();
        }
    }
}