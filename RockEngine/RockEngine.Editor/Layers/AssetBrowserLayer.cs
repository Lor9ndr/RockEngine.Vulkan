using ImGuiNET;

using NLog;

using RockEngine.Assets;
using RockEngine.Core.Assets;
using RockEngine.Core.Coroutines;
using RockEngine.Core.Rendering;
using RockEngine.Editor.EditorUI;
using RockEngine.Editor.EditorUI.ImGuiRendering;
using RockEngine.Editor.EditorUI.Thumbnails;
using RockEngine.Editor.Extensions;
using RockEngine.Editor.Helpers;
using RockEngine.Vulkan;

using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace RockEngine.Editor.Layers
{
    public class AssetBrowserLayer : ILayer
    {
        private readonly AssetManager _assetManager;
        private readonly CoroutineScheduler _coroutineScheduler;
        private readonly IAssetLoader _loader;
        private readonly IAssetSerializer _serializer;
        private readonly IThumbnailService _thumbnailService;
        private readonly ImGuiController _imGuiController;

        // UI State
        private string _searchQuery = string.Empty;
        private bool _showFileExtensions = true;
        private bool _gridView = true;
        private float _thumbnailSize = 80f;
        private string _basePath;
        private readonly HashSet<string> _selectedItems = new();

        // File System State
        private readonly List<FileSystemItem> _currentDirectoryItems = new();
        private readonly Dictionary<string, FileSystemItem> _itemCache = new();
        private bool _needsDirectoryRefresh = true;

        // Loading State
        private readonly Dictionary<string, Coroutine> _activeCoroutines = new();
        private readonly ConcurrentDictionary<Guid, Task<Thumbnail>> _pendingThumbnails = new();
        private bool _isLoadingScene = false;
        private string _loadingSceneName = "";
        private float _loadingProgress = 0f;

        // Visual State
        private readonly Dictionary<string, Vector4> _folderColors = new();
        private string _currentHoveredItem = null;
        private double _hoverStartTime = 0;
        private ImGuiSortDirection _sortDirection = ImGuiSortDirection.Ascending;
        private int _sortColumn = 0; // 0 = Name, 1 = Type, 2 = Size, 3 = Modified

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private class FileSystemItem
        {
            public string Path { get; set; }
            public string Name { get; set; }
            public string DisplayName { get; set; }
            public char Icon { get; set; } = Icons.File;
            public bool IsDirectory { get; set; }
            public bool IsAssetFile { get; set; }
            public bool IsLoading { get; set; }
            public bool IsLoaded { get; set; }
            public AssetHeader? AssetHeader { get; set; } // Changed from AssetMetadata to AssetHeader
            public IAsset? Asset { get; set; }
            public FileInfo? FileInfo { get; set; }
            public DirectoryInfo? DirectoryInfo { get; set; }
            public Stopwatch? LoadTimer { get; set; }
            public string FileExtension { get; set; }
            public long FileSize { get; set; }
            public DateTime LastModified { get; set; }
            public Thumbnail? Thumbnail { get; set; }
            public bool IsThumbnailLoading { get; set; }

            // Unique ID that considers folder names for same-named folders
            public string UniqueId => IsDirectory ? $"DIR_{Name}_{System.IO.Path.GetDirectoryName(Path)?.GetHashCode()}" : $"FILE_{Path}";
        }

        // File type icons and handling
        private static readonly Dictionary<string, char> _fileTypeIcons = new(StringComparer.OrdinalIgnoreCase)
        {
            // Asset files
            [".asset"] = '\uf1b2', // fa-cube

            // Image files
            [".png"] = '\uf1c5',
            [".jpg"] = '\uf1c5',
            [".jpeg"] = '\uf1c5',
            [".bmp"] = '\uf1c5',
            [".tga"] = '\uf1c5',
            [".tiff"] = '\uf1c5',
            [".psd"] = '\uf1c5',
            [".hdr"] = '\uf1c5',

            // 3D model files
            [".fbx"] = '\uf1b2',
            [".obj"] = '\uf1b2',
            [".blend"] = '\uf1b2',
            [".max"] = '\uf1b2',
            [".ma"] = '\uf1b2',
            [".mb"] = '\uf1b2',
            [".3ds"] = '\uf1b2',
            [".dae"] = '\uf1b2',

            // Audio files
            [".wav"] = '\uf1c7',
            [".mp3"] = '\uf1c7',
            [".ogg"] = '\uf1c7',
            [".flac"] = '\uf1c7',

            // Video files
            [".mp4"] = '\uf1c8',
            [".avi"] = '\uf1c8',
            [".mov"] = '\uf1c8',
            [".mkv"] = '\uf1c8',

            // Document files
            [".txt"] = Icons.FileAlt,
            [".doc"] = Icons.FileWord,
            [".docx"] = Icons.FileWord,
            [".pdf"] = Icons.FilePdf,
            [".md"] = Icons.FileAlt,

            // Code files
            [".cs"] = Icons.Code,
            [".js"] = Icons.Code,
            [".ts"] = Icons.Code,
            [".glsl"] = Icons.Code,

            // Configuration files
            [".json"] = Icons.Code,
            [".xml"] = Icons.Code,
            [".yaml"] = Icons.Code,
            [".yml"] = Icons.Code,
            [".config"] = Icons.Code,

            // Archive files
            [".zip"] = Icons.FileArchive,
            [".rar"] = Icons.FileArchive,
            [".7z"] = Icons.FileArchive,
            [".tar"] = Icons.FileArchive,
            [".gz"] = Icons.FileArchive,
        };

        // Font Awesome icons for asset types
        private static readonly Dictionary<string, char> _assetTypeIcons = new(StringComparer.OrdinalIgnoreCase)
        {
            ["material"] = Icons.PaintBrush,
            ["texture"] = Icons.FileImage,
            ["mesh"] = Icons.Cube,
            ["model"] = Icons.Cube,
            ["scene"] = Icons.Sitemap,
            ["shader"] = Icons.Code,
            ["script"] = Icons.FileCode,
            ["prefab"] = Icons.Cube,
            ["animation"] = Icons.Magic,
            ["audio"] = Icons.FileAudio,
            ["video"] = Icons.FileVideo,
            ["font"] = Icons.Font,
        };

        public AssetBrowserLayer(
            AssetManager assetManager,
            CoroutineScheduler coroutineScheduler,
            IAssetLoader loader,
            IAssetSerializer serializer, IThumbnailService thumbnailService,
            ImGuiController imGuiController)
        {
            _assetManager = assetManager;
            _coroutineScheduler = coroutineScheduler;
            _loader = loader;
            _serializer = serializer;
            _thumbnailService = thumbnailService;
            _imGuiController = imGuiController;
            try
            {
                // Subscribe to events
                _assetManager.OnProjectLoaded += OnProjectLoaded;
                _assetManager.OnAssetChanged += OnAssetChanged;
                _assetManager.OnProjectUnloaded += OnProjectUnloaded;

                // Set initial base path
                _basePath = string.IsNullOrEmpty(assetManager.BasePath)
                    ? Directory.GetCurrentDirectory()
                    : assetManager.BasePath;

                // Ensure the path exists
                if (!Directory.Exists(_basePath))
                {
                    _logger.Warn("Initial base path does not exist: {Path}, falling back to current directory", _basePath);
                    _basePath = Directory.GetCurrentDirectory();
                }

                _logger.Info("AssetBrowserLayer initialized with path: {Path}", _basePath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize AssetBrowserLayer");
                _basePath = Directory.GetCurrentDirectory();
            }
        }

        public void OnImGuiRender(UploadBatch vkCommandBuffer)
        {
            RenderMainWindow();
        }

        private void RenderMainWindow()
        {
            // Apply window styling
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 10));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 4.0f);

            ImGui.Begin("Asset Browser", ImGuiWindowFlags.MenuBar);

            // Draw menu bar
            DrawMenuBar();

            // Draw top toolbar (breadcrumb + search/filter)
            DrawToolbar();

            // Draw main content area
            DrawContentArea();

            // Draw status bar
            DrawStatusBar();

            // Draw loading modal (if active)
            DrawLoadingModal();

            // Draw context menus (handled per item, but we may have global ones)

            ImGui.End();

            ImGui.PopStyleVar(4);
        }

        private void DrawToolbar()
        {
            // Use a child to group toolbar elements
            ImGui.BeginChild("##Toolbar", new Vector2(0, ImGui.GetFrameHeightWithSpacing() * 2.5f),  ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);

            // Row 1: Breadcrumb + path bar
            DrawPathBar();

            // Row 2: Search + filter + view controls
            DrawSearchAndViewControls();

            ImGui.EndChild();
        }

        private void DrawPathBar()
        {
            float availableWidth = ImGui.GetContentRegionAvail().X;

            // Home button
            if (ImguiExtensions.IconButton("\uf015", "Home"))
            {
                _basePath = Directory.GetCurrentDirectory();
                _needsDirectoryRefresh = true;
                _selectedItems.Clear();
            }
            ImGui.SameLine();

            // Up one level button
            if (ImguiExtensions.IconButton("\uf062", "Up") && Directory.GetParent(_basePath) != null)
            {
                _basePath = Directory.GetParent(_basePath).FullName;
                _needsDirectoryRefresh = true;
                _selectedItems.Clear();
            }
            ImGui.SameLine();

            // Editable path text with dropdown
            ImGui.SetNextItemWidth(availableWidth - 100); // leave space for buttons
            string pathBuffer = _basePath ?? "";
            if (ImGui.InputText("##Path", ref pathBuffer, 260, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                if (Directory.Exists(pathBuffer))
                {
                    _basePath = pathBuffer;
                    _needsDirectoryRefresh = true;
                    _selectedItems.Clear();
                }
            }

            // Dropdown for quick access (optional: bookmarked folders)
            ImGui.SameLine();
            if (ImguiExtensions.IconButton("\uf0d7", "Recent"))
            {
                ImGui.OpenPopup("##PathPopup");
            }

            if (ImGui.BeginPopup("##PathPopup"))
            {
                // Add some quick paths (e.g., project root, assets folder, etc.)
                if (ImGui.MenuItem("Project Root"))
                {
                    _basePath = _assetManager.BasePath;
                    _needsDirectoryRefresh = true;
                }
                // Could add more items
                ImGui.EndPopup();
            }

            // Refresh button
            ImGui.SameLine();
            if (ImguiExtensions.IconButton("\uf021", "Refresh"))
            {
                _needsDirectoryRefresh = true;
            }
        }

        private void DrawSearchAndViewControls()
        {
            // Search input with icon
            ImGui.AlignTextToFramePadding();
            ImGui.Text("\uf002"); // search icon
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200);
            ImGui.InputTextWithHint("##Search", "Search...", ref _searchQuery, 100);

            ImGui.SameLine();

            // Filter by type dropdown
            ImGui.SetNextItemWidth(120);
            string[] filterTypes = { "All", "Folders", "Assets", "Images", "Models", "Audio", "Documents" };
            int currentFilter = 0; // placeholder; you'd need a field for this
            ImGui.Combo("##Filter", ref currentFilter, filterTypes, filterTypes.Length);

            ImGui.SameLine();

            // Sort dropdown
            ImGui.SetNextItemWidth(120);
            string[] sortOptions = { "Name", "Size", "Date Modified", "Type" };
            int currentSort = 0; // placeholder
            bool sortAscending = true; // placeholder
            if (ImGui.BeginCombo("##Sort", sortOptions[currentSort]))
            {
                for (int i = 0; i < sortOptions.Length; i++)
                {
                    bool isSelected = (currentSort == i);
                    if (ImGui.Selectable(sortOptions[i], isSelected))
                    {
                        currentSort = i;
                        // Trigger re-sort
                    }
                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            ImGui.SameLine();

            // Thumbnail size slider (only shown in grid view)
            if (_gridView)
            {
                ImGui.SetNextItemWidth(150);
                ImGui.SliderFloat("##Size", ref _thumbnailSize, 32f, 256f, "Size: %.0f");
                ImGui.SameLine();
            }

            // View toggle buttons (grid/list)
            if (ImguiExtensions.IconButton(_gridView ? "\uf00a" : "\uf03a", _gridView ? "Switch to List" : "Switch to Grid"))
            {
                _gridView = !_gridView;
            }

            // Optional: toggle file extensions
            ImGui.SameLine();
            ImGui.Checkbox("Ext", ref _showFileExtensions);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show file extensions");
        }

        private void DrawContentArea()
        {
            float contentHeight = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing(); // reserve space for status bar
            ImGui.BeginChild("##AssetBrowserContent", new Vector2(0, contentHeight), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar);

            if (!Directory.Exists(_basePath))
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "Directory does not exist!");
                if (ImGui.Button("Reset to Current Directory"))
                {
                    _basePath = Directory.GetCurrentDirectory();
                    _needsDirectoryRefresh = true;
                }
            }

            if (_needsDirectoryRefresh)
            {
                StartDirectoryRefreshCoroutine();
                _needsDirectoryRefresh = false;
            }

            if (_currentDirectoryItems.Count == 0)
            {
                ImguiExtensions.CenteredText("No items found...");
            }
            else
            {
                if (_gridView)
                {
                    DrawGridView();
                }
                else
                {
                    DrawListView();
                }
            }

            ImGui.EndChild();
        }
        private void StartDirectoryRefreshCoroutine()
        {
            if (_activeCoroutines.TryGetValue("directory_refresh", out var existingCoroutine))
            {
                _coroutineScheduler.StopCoroutine(existingCoroutine);
            }

            var refreshCoroutine = _coroutineScheduler.StartCoroutine(
                RefreshDirectoryCoroutine(),
                "RefreshDirectory"
            );

            _activeCoroutines["directory_refresh"] = refreshCoroutine;
        }
        private IEnumerator RefreshDirectoryCoroutine()
        {
            _logger.Debug("Starting directory refresh coroutine for: {Path}", _basePath);

            _currentDirectoryItems.Clear();

            if (!Directory.Exists(_basePath))
            {
                _logger.Warn("Directory does not exist: {Path}", _basePath);
                yield break;
            }

            yield return new WaitForNextFrame();

            string[] directories = Array.Empty<string>();
            string[] allFiles = Array.Empty<string>();

            directories = Directory.GetDirectories(_basePath);
            _logger.Debug("Found {Count} directories in {Path}", directories.Length, _basePath);

            foreach (var dir in directories)
            {
                var dirInfo = new DirectoryInfo(dir);
                var item = CreateDirectoryItem(dirInfo);
                _currentDirectoryItems.Add(item);
            }

            yield return new WaitForNextFrame();

            allFiles = Directory.GetFiles(_basePath);
            _logger.Debug("Found {Count} files in {Path}", allFiles.Length, _basePath);

            int filesProcessed = 0;

            foreach (var file in allFiles)
            {
                var fileInfo = new FileInfo(file);
                var cacheKey = fileInfo.FullName;
                if (!_itemCache.TryGetValue(cacheKey, out var item))
                {
                    item = CreateFileItem(fileInfo);
                    _itemCache[cacheKey] = item;

                    if (item.IsAssetFile)
                    {
                        StartAssetHeaderCoroutine(item);
                    }
                }

                _currentDirectoryItems.Add(item);
                filesProcessed++;

                if (filesProcessed % 20 == 0)
                    yield return new WaitForNextFrame();
            }

            _currentDirectoryItems.Sort((a, b) =>
            {
                if (a.IsDirectory && !b.IsDirectory) return -1;
                if (!a.IsDirectory && b.IsDirectory) return 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            _logger.Debug("Directory refresh completed. Found {DirCount} directories, {FileCount} files",
                directories.Length, allFiles.Length);
        }

        private void DrawGridView()
        {
            // Reduce cell padding to tighten vertical spacing
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4, 2));

            float availableWidth = ImGui.GetContentRegionAvail().X;
            float itemWidth = _thumbnailSize + ImGui.GetStyle().ItemSpacing.X + 20; // extra for text
            int columns = Math.Max(1, (int)(availableWidth / itemWidth));

            if (ImGui.BeginTable("##AssetGrid", columns, ImGuiTableFlags.SizingFixedFit))
            {
                foreach (var item in _currentDirectoryItems)
                {
                    if (ShouldFilterItem(item)) continue;

                    ImGui.TableNextColumn();
                    ImGui.PushID(item.UniqueId);
                    DrawGridItem(item);
                    ImGui.PopID();
                }
                ImGui.EndTable();
            }

            ImGui.PopStyleVar();
        }

        private void DrawGridItem(FileSystemItem item)
        {
            bool isSelected = _selectedItems.Contains(item.Path);
            bool isHovered = _currentHoveredItem == item.Path;

            var drawList = ImGui.GetWindowDrawList();
            var cursorPos = ImGui.GetCursorScreenPos();
            var cardSize = new Vector2(_thumbnailSize + 20, _thumbnailSize + 40);
            var cardMin = cursorPos;
            var cardMax = cursorPos + cardSize;

            // Background
            uint bgColor = ImGui.GetColorU32(isSelected ? ImGuiCol.Header : ImGuiCol.WindowBg);
            if (isHovered && !isSelected)
                bgColor = ImGui.GetColorU32(ImGuiCol.HeaderHovered);
            drawList.AddRectFilled(cardMin, cardMax, bgColor, 6.0f);
            drawList.AddRect(cardMin, cardMax, ImGui.GetColorU32(ImGuiCol.Border), 6.0f);

            // Thumbnail area
            var thumbPos = cursorPos + new Vector2(10, 10);
            var thumbSize = new Vector2(_thumbnailSize, _thumbnailSize);

            // Decide what to draw
            if (item.IsLoading || item.IsThumbnailLoading)
            {
                ImguiExtensions.Spinner(thumbPos + thumbSize * 0.5f, 10, 2, ImGui.GetColorU32(ImGuiCol.Text));
            }
            else if (item.Thumbnail != null)
            {
                var textureId = _imGuiController.GetTextureID(item.Thumbnail.Texture);
                drawList.AddImage(textureId, thumbPos, thumbPos + thumbSize);
            }
            else
            {
                // Fallback icon
                string iconText = item.Icon.ToString();
                var textSize = ImGui.CalcTextSize(iconText);
                var textPos = thumbPos + (thumbSize - textSize) * 0.5f;
                drawList.AddText(textPos, ImGui.GetColorU32(ImGuiCol.Text), iconText);
            }

          

            // If this is a texture asset and thumbnail not yet loaded, start loading
            if (item.IsAssetFile && item.AssetHeader?.AssetType == typeof(TextureAsset) && item.Thumbnail == null && !item.IsThumbnailLoading)
            {
                StartThumbnailLoadingCoroutine(item);
            }


            string displayName = GetDisplayName(item);
            float maxNameWidth = thumbSize.X + 20; // same as before
            Vector2 nameSize = ImGui.CalcTextSize(displayName);

            if (nameSize.X > maxNameWidth)
            {
                // Find how many characters fit
                string ellipsis = "...";
                float ellipsisWidth = ImGui.CalcTextSize(ellipsis).X;
                float availableWidth = maxNameWidth - ellipsisWidth;

                // Binary search for the longest prefix that fits
                int low = 0, high = displayName.Length;
                int best = 0;
                while (low <= high)
                {
                    int mid = (low + high) / 2;
                    string sub = displayName.Substring(0, mid);
                    float w = ImGui.CalcTextSize(sub).X;
                    if (w <= availableWidth)
                    {
                        best = mid;
                        low = mid + 1;
                    }
                    else
                    {
                        high = mid - 1;
                    }
                }
                displayName = string.Concat(displayName.AsSpan(0, best), ellipsis);
            }

            var namePos = cursorPos + new Vector2(10, 10 + _thumbnailSize + 5);
            drawList.AddText(namePos, ImGui.GetColorU32(ImGuiCol.Text), displayName);

            // Reserve the exact card size using a dummy placed at the top of the cell
            ImGui.SetCursorScreenPos(cursorPos);
            ImGui.Dummy(cardSize);   // this advances cursor to cardMax.Y automatically

            // Interaction (uses the same rect as the dummy)
            if (ImGui.IsMouseHoveringRect(cardMin, cardMax))
            {
                _currentHoveredItem = item.Path;
                _hoverStartTime = ImGui.GetTime();

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    HandleItemClick(item);
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    HandleItemDoubleClick(item);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                {
                    if (!isSelected) _selectedItems.Clear();
                    _selectedItems.Add(item.Path);
                    ImGui.OpenPopup("##ItemContextMenu");
                }
                if (ImGui.IsMouseDragging(ImGuiMouseButton.Left) && isSelected)
                    HandleItemDragDrop(item);
            }

            // Context menu
            if (ImGui.BeginPopup("##ItemContextMenu"))
            {
                DrawItemContextMenu(item);
                ImGui.EndPopup();
            }

            // Tooltip
            if (_currentHoveredItem == item.Path && ImGui.GetTime() - _hoverStartTime > 0.5)
                DrawItemTooltip(item);
        }
        private void StartThumbnailLoadingCoroutine(FileSystemItem item)
        {
            var assetId = item.AssetHeader!.AssetId;

            item.IsThumbnailLoading = true;

            _coroutineScheduler.StartCoroutine(
                LoadThumbnailCoroutine(item, assetId),
                $"LoadThumbnail_{assetId}"
            );
           
        }

        private IEnumerator LoadThumbnailCoroutine(FileSystemItem item, Guid assetId)
        {
            // Start the async thumbnail creation
            var assetTask = _assetManager.GetAssetAsync<IAsset>(assetId);
            yield return new WaitForTask<IAsset>(assetTask);

            var thumbnailTask = _thumbnailService.GetOrCreateThumbnailAsync(
                 assetTask.Result 
            );

            yield return new WaitForTask<Thumbnail>(thumbnailTask);
            var thumbnail = thumbnailTask.Result;
            item.Thumbnail = thumbnail;
            item.IsThumbnailLoading = false;
        }

        private void DrawItemTooltip(FileSystemItem item)
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(400f);

            if (item.IsDirectory)
            {
                ImGui.Text(item.Name);
                ImGui.Separator();
                ImGui.Text("\uf07b Folder");
                try
                {
                    var dirInfo = new DirectoryInfo(item.Path);
                    var files = dirInfo.GetFiles();
                    var dirs = dirInfo.GetDirectories();
                    ImGui.Text($"Items: {files.Length + dirs.Length}");
                }
                catch
                {
                    ImGui.Text("Items: Unknown");
                }
            }
            else
            {
                ImGui.Text(item.Name);
                ImGui.Separator();

                if (item.IsAssetFile && item.AssetHeader != null)
                {
                    ImGui.Text($"Type: {GetSimpleTypeName(item.AssetHeader.AssetTypeName)}");
                    ImGui.Text($"ID: {item.AssetHeader.AssetId}");
                }
                else
                {
                    ImGui.Text($"Type: {item.FileExtension.ToUpper().TrimStart('.')} File");
                }

                ImGui.Text($"Size: {FormatHelper.FormatFileSize(item.FileSize)}");
                ImGui.Text($"Modified: {item.LastModified:yyyy-MM-dd HH:mm:ss}");
            }

            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        private void DrawListView()
        {
            if (ImGui.BeginTable("AssetList", 4,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollY,
                new Vector2(0, ImGui.GetContentRegionAvail().Y)))
            {
                // Name column: stretch with default sort
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.DefaultSort, 0.5f);
                // Type, Size, Modified: fixed width
                ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Modified", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableHeadersRow();

                // Handle sorting
                ImGuiTableSortSpecsPtr sortSpecs = ImGui.TableGetSortSpecs();
                if (sortSpecs.SpecsDirty)
                {
                    SortItems(sortSpecs.Specs);  // pass the first sort spec
                    sortSpecs.SpecsDirty = false;
                }

                foreach (var item in _currentDirectoryItems)
                {
                    if (ShouldFilterItem(item)) continue;
                    ImGui.TableNextRow();
                    DrawListItem(item);
                }

                ImGui.EndTable();
            }
        }

        private void SortItems(in ImGuiTableColumnSortSpecsPtr spec)
        {
            _sortColumn = spec.ColumnIndex;
            _sortDirection = spec.SortDirection;

            Comparison<FileSystemItem> comparison = (a, b) =>
            {
                // Directories always come first
                if (a.IsDirectory && !b.IsDirectory) return -1;
                if (!a.IsDirectory && b.IsDirectory) return 1;

                int result = 0;
                switch (_sortColumn)
                {
                    case 0: // Name
                        result = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                        break;
                    case 1: // Type
                        string typeA = GetItemTypeString(a);
                        string typeB = GetItemTypeString(b);
                        result = string.Compare(typeA, typeB, StringComparison.OrdinalIgnoreCase);
                        break;
                    case 2: // Size
                        if (a.IsDirectory && b.IsDirectory) result = 0;
                        else if (a.IsDirectory) result = -1;
                        else if (b.IsDirectory) result = 1;
                        else result = a.FileSize.CompareTo(b.FileSize);
                        break;
                    case 3: // Modified
                        result = a.LastModified.CompareTo(b.LastModified);
                        break;
                }

                return _sortDirection == ImGuiSortDirection.Ascending ? result : -result;
            };

            _currentDirectoryItems.Sort(comparison);
        }

        private string GetItemTypeString(FileSystemItem item)
        {
            if (item.IsDirectory) return "Folder";
            if (item.IsAssetFile) return "Asset";
            return item.FileExtension.ToUpper().TrimStart('.');
        }

        private void DrawListItem(FileSystemItem item)
        {
            bool isSelected = _selectedItems.Contains(item.Path);

            ImGui.TableNextColumn();
            ImGui.PushID(item.UniqueId);

            string displayName = GetDisplayName(item);
            string label = $"{item.Icon}  {displayName}";

            // Use selectable to fill the row
            if (ImGui.Selectable(label, isSelected, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick))
            {
                HandleItemClick(item);
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    HandleItemDoubleClick(item);
                }
            }

            // Right-click context
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                if (!isSelected)
                {
                    _selectedItems.Clear();
                    _selectedItems.Add(item.Path);
                }
                ImGui.OpenPopup("##ItemContextMenu");
            }

            // Context menu
            if (ImGui.BeginPopup("##ItemContextMenu"))
            {
                DrawItemContextMenu(item);
                ImGui.EndPopup();
            }

            ImGui.PopID();

            ImGui.TableNextColumn();
            if (item.IsDirectory)
                ImGui.Text("Folder");
            else if (item.IsAssetFile)
                ImGui.Text("Asset");
            else
                ImGui.Text(item.FileExtension.ToUpper().TrimStart('.'));

            ImGui.TableNextColumn();
            if (item.IsDirectory)
                ImGui.Text("-");
            else
                ImGui.Text(FormatHelper.FormatFileSize(item.FileSize));

            ImGui.TableNextColumn();
            ImGui.Text(item.LastModified.ToString("yyyy-MM-dd HH:mm"));

            if (ImGui.IsItemHovered())
            {
                DrawItemTooltip(item);
            }
        }

        private void DrawItemContextMenu(FileSystemItem item)
        {
            if (ImGui.MenuItem("Open"))
            {
                if (item.IsDirectory)
                    HandleItemDoubleClick(item);
                else
                    OpenFileWithDefaultApplication(item.Path);
            }

            if (ImGui.MenuItem("Open in Explorer"))
            {
                try
                {
                    Process.Start("explorer.exe", $"/select,\"{item.Path}\"");
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to open in explorer");
                }
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Rename"))
            {
                // TODO: Implement rename UI
            }

            if (ImGui.MenuItem("Delete", "Del"))
            {
                try
                {
                    if (item.IsDirectory)
                        Directory.Delete(item.Path, true);
                    else
                        File.Delete(item.Path);
                    _needsDirectoryRefresh = true;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to delete {Path}", item.Path);
                }
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Copy Path"))
            {
                ImGui.SetClipboardText(item.Path);
            }

            if (ImGui.MenuItem("Copy Name"))
            {
                ImGui.SetClipboardText(item.Name);
            }

            if (item.IsAssetFile && item.AssetHeader != null)
            {
                ImGui.Separator();
                if (ImGui.MenuItem("Copy Asset ID"))
                {
                    ImGui.SetClipboardText(item.AssetHeader.AssetId.ToString());
                }
            }
        }

        private void DrawStatusBar()
        {
            ImGui.Separator();
            ImGui.BeginChild("##StatusBar", new Vector2(0, ImGui.GetFrameHeightWithSpacing()),  ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);

            int totalItems = _currentDirectoryItems.Count;
            int filteredItems = _currentDirectoryItems.Count(item => !ShouldFilterItem(item));
            int selectedCount = _selectedItems.Count;

            ImGui.Text($"Items: {filteredItems} / {totalItems}  |  Selected: {selectedCount}");

            if (!string.IsNullOrEmpty(_searchQuery))
            {
                ImGui.SameLine();
                ImGui.Text($"  |  Search: \"{_searchQuery}\"");
            }

            ImGui.SameLine(ImGui.GetWindowWidth() - 150);
            ImGui.Text(_basePath);

            ImGui.EndChild();
        }

        private FileSystemItem CreateDirectoryItem(DirectoryInfo dirInfo)
        {
            return new FileSystemItem
            {
                Path = dirInfo.FullName,
                Name = dirInfo.Name,
                DisplayName = dirInfo.Name,
                Icon = Icons.Folder,
                IsDirectory = true,
                IsAssetFile = false,
                IsLoaded = true,
                DirectoryInfo = dirInfo,
                LastModified = dirInfo.LastWriteTime
            };
        }

        private FileSystemItem CreateFileItem(FileInfo fileInfo)
        {
            var extension = fileInfo.Extension.ToLower();
            var isAssetFile = extension == AssetConstants.AssetExtension;

            return new FileSystemItem
            {
                Path = fileInfo.FullName,
                Name = fileInfo.Name,
                DisplayName = Path.GetFileNameWithoutExtension(fileInfo.Name),
                Icon = GetFileIcon(extension),
                IsDirectory = false,
                IsAssetFile = isAssetFile,
                IsLoaded = !isAssetFile,
                IsLoading = isAssetFile,
                FileInfo = fileInfo,
                FileExtension = extension,
                FileSize = fileInfo.Length,
                LastModified = fileInfo.LastWriteTime
            };
        }

        private char GetFileIcon(string extension)
        {
            return _fileTypeIcons.TryGetValue(extension, out var icon)
                ? icon
                : '\uf15b';
        }

        private void StartAssetHeaderCoroutine(FileSystemItem item)
        {
            var coroutineKey = $"metadata_{item.UniqueId}";

            if (_activeCoroutines.TryGetValue(coroutineKey, out var existingCoroutine))
            {
                _coroutineScheduler.StopCoroutine(existingCoroutine);
            }

            item.IsLoading = true;
            item.LoadTimer = Stopwatch.StartNew();

            var metadataCoroutine = _coroutineScheduler.StartCoroutine(
                LoadAssetHeaderCoroutine(item),
                $"LoadMetadata_{item.UniqueId}"
            );

            _activeCoroutines[coroutineKey] = metadataCoroutine;

            metadataCoroutine.OnCompleted += (coroutine) =>
            {
                _activeCoroutines.Remove(coroutineKey);
                _logger.Debug("Asset header loaded for: {Asset}", item.Name);
            };

            metadataCoroutine.OnError += (coroutine, error) =>
            {
                _activeCoroutines.Remove(coroutineKey);
                item.Icon = Icons.Times;
                item.IsLoading = false;
                _logger.Warn("Failed to load asset header for {Asset}: {Error}", item.Name, error.Message);
            };
        }

        private IEnumerator LoadAssetHeaderCoroutine(FileSystemItem item)
        {
            _logger.Debug("Starting asset header coroutine for: {Asset}", item.Name);

           
                using var stream = File.OpenRead(item.Path);
                var headerTask = _serializer.DeserializeHeaderAsync(stream);
                yield return new WaitForTask(headerTask);

            try
            {
                if (headerTask.IsCompletedSuccessfully)
                {
                    var header = headerTask.Result;
                    item.AssetHeader = header;
                    item.Icon = GetAssetTypeIcon(GetSimpleTypeName(header.AssetTypeName));
                    item.IsLoaded = true;
                    item.IsLoading = false;

                    _logger.Debug("Asset header loaded successfully for: {Asset} ({Type})",
                        item.Name, header.AssetTypeName);
                }
                else
                {
                    throw headerTask.Exception ?? new Exception("Failed to load asset header");
                }
            }
            catch (Exception ex)
            {
                item.Icon = Icons.Times;
                item.IsLoading = false;
                throw new Exception($"Failed to load asset header for {item.Name}", ex);
            }
        }

        private string GetSimpleTypeName(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName))
                return "Unknown";

            var typeName = assemblyQualifiedName;
            var commaIndex = typeName.IndexOf(',');
            if (commaIndex > 0)
            {
                typeName = typeName[..commaIndex];
            }

            var lastDot = typeName.LastIndexOf('.');
            if (lastDot > 0)
            {
                typeName = typeName[(lastDot + 1)..];
            }

            if (typeName.EndsWith("Asset", StringComparison.OrdinalIgnoreCase))
            {
                typeName = typeName[..^5];
            }

            return typeName;
        }

        private char GetAssetTypeIcon(string assetType)
        {
            return _assetTypeIcons.TryGetValue(assetType?.ToLower() ?? "", out var icon)
                ? icon
                : Icons.Cube;
        }

        private void HandleItemClick(FileSystemItem item)
        {
            if (ImGui.GetIO().KeyCtrl)
            {
                if (!_selectedItems.Remove(item.Path))
                {
                    _selectedItems.Add(item.Path);
                }
            }
            else
            {
                _selectedItems.Clear();
                _selectedItems.Add(item.Path);
            }
        }

        private void HandleItemDoubleClick(FileSystemItem item)
        {
            if (item.IsDirectory)
            {
                _basePath = item.Path;
                _needsDirectoryRefresh = true;
                _selectedItems.Clear();
            }
            else if (item.IsAssetFile)
            {
                // Handle asset file double-click
                OpenAssetFile(item);
            }
            else
            {
                OpenFileWithDefaultApplication(item.Path);
            }
        }

        private void OpenAssetFile(FileSystemItem item)
        {
            // You need to implement this based on your asset types
            // For example, if it's a scene, you might load it
            if (item.IsAssetFile && item.AssetHeader?.AssetTypeName.Contains("Scene", StringComparison.OrdinalIgnoreCase) == true)
            {
                StartSceneLoadingCoroutine(item.Path, item.Name);

            }
        }
        private void StartSceneLoadingCoroutine(string scenePath, string sceneName)
        {
            var coroutineKey = $"scene_load_{scenePath.GetHashCode()}";

            if (_activeCoroutines.TryGetValue(coroutineKey, out var existingCoroutine))
            {
                _coroutineScheduler.StopCoroutine(existingCoroutine);
            }

            var sceneCoroutine = _coroutineScheduler.StartCoroutine(
                LoadSceneCoroutine(scenePath, sceneName),
                $"LoadScene_{sceneName}"
            );

            _activeCoroutines[coroutineKey] = sceneCoroutine;

            sceneCoroutine.OnCompleted += (coroutine) =>
            {
                _activeCoroutines.Remove(coroutineKey);
                _logger.Info("Scene loaded successfully: {Scene}", sceneName);
            };

            sceneCoroutine.OnError += (coroutine, error) =>
            {
                _activeCoroutines.Remove(coroutineKey);
                _logger.Error(error, "Failed to load scene: {Scene}", sceneName);
            };
        }

        private IEnumerator LoadSceneCoroutine(string scenePath, string sceneName)
        {
            _isLoadingScene = true;
            _loadingSceneName = sceneName;
            _loadingProgress = 0f;

            _logger.Info("Starting scene load coroutine: {Scene}", sceneName);

            try
            {
                _loadingProgress = 0.1f;
                yield return new WaitForNextFrame();

                var loadTask = _assetManager.LoadAssetAsync<SceneAsset>(scenePath);

                while (!loadTask.IsCompleted)
                {
                    _loadingProgress = 0.1f + (0.5f * GetEstimatedProgress(loadTask));
                    yield return new WaitForNextFrame();
                }

                yield return new WaitForTask(loadTask);

                if (loadTask.IsCompletedSuccessfully)
                {
                    var sceneAsset = loadTask.Result;
                    var loadDataTask = _assetManager.LoadAssetDataAsync(sceneAsset);
                    while (!loadDataTask.IsCompleted)
                    {
                        _loadingProgress = 0.5f + (0.9f * GetEstimatedProgress(loadDataTask));
                        yield return new WaitForNextFrame();
                    }
                    yield return new WaitForTask(loadDataTask);
                    if (!loadDataTask.IsCompletedSuccessfully)
                    {
                        throw new Exception($"Failed to load scene asset: {scenePath}", loadDataTask.Exception);
                    }

                    yield return new WaitForNextFrame();
                    var progress = new Progress<int>(percent =>
                    {
                        // Map percent from 0-100 to 0.6-0.95 range
                        _loadingProgress = 0.6f + (percent / 100f) * 0.35f;
                    });
                    var sceneInitTask = Task.Run(()=>sceneAsset.InstantiateEntities(progress));

                    yield return new WaitForTask(sceneInitTask);
                    _loadingProgress = 0.95f;

                    if (!sceneInitTask.IsCompletedSuccessfully)
                    {
                        throw new Exception($"Failed to InstantiateEntities: {scenePath}", sceneInitTask.Exception);
                    }

                    _loadingProgress = 1.0f;
                    yield return new WaitForNextFrame();

                    _logger.Info("Scene instantiated: {Scene}", sceneAsset.Name);
                }
                else
                {
                    throw new Exception($"Failed to load scene asset: {scenePath}", loadTask.Exception);
                }


            }
            finally
            {
                _isLoadingScene = false;
                _loadingProgress = 0f;
            }
        }
        private float GetEstimatedProgress(Task task)
        {
            if (task.IsCompleted) return 1.0f;
            return Math.Min(0.95f, DateTime.Now.Ticks % 1000000 / 1000000f);
        }


        private void HandleItemDragDrop(FileSystemItem item)
        {
          
        }

        private void OpenFileWithDefaultApplication(string filePath)
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                };
                Process.Start(processStartInfo);
            }
            catch (Exception ex)
            {
                _logger.Warn("Failed to open file {File}: {Error}", filePath, ex.Message);
            }
        }

        private bool ShouldFilterItem(FileSystemItem item)
        {
            if (string.IsNullOrEmpty(_searchQuery)) return false;

            var searchText = _searchQuery.ToLowerInvariant();
            var itemName = item.Name.ToLowerInvariant();

            return !itemName.Contains(searchText);
        }

        private string GetDisplayName(FileSystemItem item)
        {
            if (item.IsDirectory)
                return item.Name;

            return _showFileExtensions
                ? item.Name
                : item.DisplayName;
        }

       

        private void DrawLoadingModal()
        {
            if (_isLoadingScene)
            {
                ImGui.OpenPopup("Loading Scene");

                Vector2 center = ImGui.GetMainViewport().GetCenter();
                ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

                if (ImGui.BeginPopupModal("Loading Scene", ref _isLoadingScene,
                    ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
                {
                    ImguiExtensions.CenteredText($"Loading: {_loadingSceneName}");
                    ImguiExtensions.CenteredProgressBar(_loadingProgress, new Vector2(200, 20));
                    ImGui.EndPopup();
                }
            }
        }

        private void DrawContextMenus()
        {
            // Implement context menus if needed
        }

        private void DrawMenuBar()
        {
            if (ImGui.BeginMenuBar())
            {
                if (ImGui.BeginMenu("File"))
                {
                    if (ImGui.MenuItem("\uf65e New Folder"))
                    {
                        CreateNewFolder();
                    }
                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("View"))
                {
                    ImGui.MenuItem("\uf15c Show File Extensions", "", ref _showFileExtensions);
                    ImGui.MenuItem("\uf00a Grid View", "", ref _gridView);
                    ImGui.EndMenu();
                }

                ImGui.EndMenuBar();
            }
        }

        private void CreateNewFolder()
        {
            try
            {
                int folderNumber = 1;
                string newFolderName = "New Folder";
                string fullPath = Path.Combine(_basePath, newFolderName);

                while (Directory.Exists(fullPath))
                {
                    folderNumber++;
                    newFolderName = $"New Folder ({folderNumber})";
                    fullPath = Path.Combine(_basePath, newFolderName);
                }

                Directory.CreateDirectory(fullPath);
                _needsDirectoryRefresh = true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to create new folder");
            }
        }

        public void OnUpdate()
        {
            // Coroutines are processed by the Application's CoroutineScheduler
        }

        public void OnRender(UploadBatch vkCommandBuffer)
        {
            // 3D rendering logic would go here if needed
        }

        public Task OnAttach()
        {
            _logger.Info("AssetBrowserLayer attached");
            _needsDirectoryRefresh = true;
            return Task.CompletedTask;
        }

        public void OnDetach()
        {
            _logger.Info("AssetBrowserLayer detached");

            foreach (var coroutine in _activeCoroutines.Values)
            {
                _coroutineScheduler.StopCoroutine(coroutine);
            }
            _activeCoroutines.Clear();

            _assetManager.OnProjectLoaded -= OnProjectLoaded;
            _assetManager.OnAssetChanged -= OnAssetChanged;
            _assetManager.OnProjectUnloaded -= OnProjectUnloaded;
        }

        private void OnProjectLoaded(IProject project)
        {
            _basePath = _assetManager.BasePath;
            _needsDirectoryRefresh = true;
            _itemCache.Clear();

            foreach (var coroutine in _activeCoroutines.Values)
            {
                _coroutineScheduler.StopCoroutine(coroutine);
            }
            _activeCoroutines.Clear();

            _logger.Info("Project loaded, base path: {BasePath}", _basePath);
        }

        private void OnProjectUnloaded()
        {
            _basePath = Directory.GetCurrentDirectory();
            _needsDirectoryRefresh = true;
            _itemCache.Clear();
            _currentDirectoryItems.Clear();
            _selectedItems.Clear();

            foreach (var coroutine in _activeCoroutines.Values)
            {
                _coroutineScheduler.StopCoroutine(coroutine);
            }
            _activeCoroutines.Clear();

            _logger.Info("Project unloaded, reset to: {BasePath}", _basePath);
        }

        private void OnAssetChanged(AssetChangedEventArgs args)
        {
            _logger.Debug("Asset changed: {AssetName} ({ChangeType}) at {Path}",
                args.Asset.Name, args.ChangeType, args.Path);

            var normalizedPath = AssetPathNormalizer.Normalize(args.Path);

            // Update cache if this item is cached
            if (_itemCache.TryGetValue(normalizedPath, out var cachedItem))
            {
                cachedItem.AssetHeader = new AssetHeader
                {
                    AssetId = args.Asset.ID,
                    AssetTypeName = args.Asset.GetType().AssemblyQualifiedName,
                    Name = args.Asset.Name,
                    Created = args.Asset.Created,
                    Modified = args.Asset.Modified
                };
                cachedItem.Icon = GetAssetTypeIcon(GetSimpleTypeName(cachedItem.AssetHeader.AssetTypeName));
                cachedItem.IsLoaded = true;
                cachedItem.IsLoading = false;
            }

            // Refresh if this is in the current directory
            var directory = Path.GetDirectoryName(normalizedPath);
            if (!string.IsNullOrEmpty(directory) &&
                Path.GetFullPath(directory) == Path.GetFullPath(_basePath))
            {
                _needsDirectoryRefresh = true;
            }
        }
    }
}