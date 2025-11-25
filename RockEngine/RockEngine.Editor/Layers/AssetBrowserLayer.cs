using ImGuiNET;

using NLog;

using RockEngine.Core;
using RockEngine.Core.Assets;
using RockEngine.Core.Coroutines;
using RockEngine.Core.Rendering;
using RockEngine.Editor.EditorUI;
using RockEngine.Editor.EditorUI.ImGuiRendering;
using RockEngine.Vulkan;

using Silk.NET.SDL;

using System.Collections;
using System.Diagnostics;
using System.Numerics;

namespace RockEngine.Editor.Layers
{
    public class AssetBrowserLayer : ILayer
    {
        private readonly AssetManager _assetManager;
        private readonly CoroutineScheduler _coroutineScheduler;

        // UI State
        private string _searchQuery = string.Empty;
        private bool _showFileExtensions = true;
        private bool _gridView = true;
        private float _thumbnailSize = 80f;
        private string _basePath;
        private readonly HashSet<string> _selectedItems = new HashSet<string>();

        // File System State
        private readonly List<FileSystemItem> _currentDirectoryItems = new List<FileSystemItem>();
        private readonly Dictionary<string, FileSystemItem> _itemCache = new Dictionary<string, FileSystemItem>();
        private bool _needsDirectoryRefresh = true;

        // Loading State
        private readonly Dictionary<string, Coroutine> _activeCoroutines = new Dictionary<string, Coroutine>();
        private bool _isLoadingScene = false;
        private string _loadingSceneName = "";
        private float _loadingProgress = 0f;

        // Visual State
        private readonly Dictionary<string, Vector4> _folderColors = new Dictionary<string, Vector4>();
        private string _currentHoveredItem = null;
        private double _hoverStartTime = 0;

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private class FileSystemItem
        {
            public string Path { get; set; }
            public string Name { get; set; }
            public string DisplayName { get; set; }
            public char Icon { get; set; } = Icons.File; // fa-file
            public bool IsDirectory { get; set; }
            public bool IsAssetFile { get; set; }
            public bool IsLoading { get; set; }
            public bool IsLoaded { get; set; }
            public AssetMetadataInfo Metadata { get; set; }
            public FileInfo FileInfo { get; set; }
            public DirectoryInfo DirectoryInfo { get; set; }
            public Stopwatch LoadTimer { get; set; }
            public string FileExtension { get; set; }
            public long FileSize { get; set; }
            public DateTime LastModified { get; set; }

            // Unique ID that considers folder names for same-named folders
            public string UniqueId => IsDirectory ? $"DIR_{Name}_{System.IO.Path.GetDirectoryName(Path)?.GetHashCode()}" : $"FILE_{Path}";
        }

        // File type icons and handling
        private static readonly Dictionary<string, char> _fileTypeIcons = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase)
        {
            // Asset files
           [".asset"] = '\uf1b2', // fa-cube

            // Image files
            [".png"] = '\uf1c5',   // fa-file-image
            [".jpg"] = '\uf1c5',   // fa-file-image
            [".jpeg"] = '\uf1c5',  // fa-file-image
            [".bmp"] = '\uf1c5',   // fa-file-image
            [".tga"] = '\uf1c5',   // fa-file-image
            [".tiff"] = '\uf1c5',  // fa-file-image
            [".psd"] = '\uf1c5',   // fa-file-image
            [".hdr"] = '\uf1c5',   // fa-file-image

            // 3D model files
            [".fbx"] = '\uf1b2',   // fa-cube
            [".obj"] = '\uf1b2',   // fa-cube
            [".blend"] = '\uf1b2', // fa-cube
            [".max"] = '\uf1b2',   // fa-cube
            [".ma"] = '\uf1b2',    // fa-cube
            [".mb"] = '\uf1b2',    // fa-cube
            [".3ds"] = '\uf1b2',   // fa-cube
            [".dae"] = '\uf1b2',   // fa-cube

            // Audio files
            [".wav"] = '\uf1c7',   // fa-file-audio
            [".mp3"] = '\uf1c7',   // fa-file-audio
            [".ogg"] = '\uf1c7',   // fa-file-audio
            [".flac"] = '\uf1c7',  // fa-file-audio

            // Video files
            [".mp4"] = '\uf1c8',   // fa-file-video
            [".avi"] = '\uf1c8',   // fa-file-video
            [".mov"] ='\uf1c8',   // fa-file-video
            [".mkv"] ='\uf1c8',   // fa-file-video

            // Document files
            [".txt"] = Icons.FileAlt,   // fa-file-alt
            [".doc"] = Icons.FileWord,   // fa-file-word
            [".docx"] = Icons.FileWord,  // fa-file-word
            [".pdf"] = Icons.FilePdf,   // fa-file-pdf
            [".md"] = Icons.FileAlt,    // fa-file-alt

            // Code files
            [".cs"] = Icons.Code,    // fa-code
            [".js"] = Icons.Code,    // fa-code
            [".ts"] = Icons.Code,    // fa-code
            [".glsl"] = Icons.Code,  // fa-code

            // Configuration files
            [".json"] =   Icons.Code,  // fa-file-code
            [".xml"] =   Icons.Code,   // fa-file-code
            [".yaml"] =  Icons.Code,  // fa-file-code
            [".yml"] =   Icons.Code,   // fa-file-code
            [".config"] = Icons.Code,// fa-file-code

            // Archive files
            [".zip"] = Icons.FileArchive,   // fa-file-archive
            [".rar"] = Icons.FileArchive,   // fa-file-archive
            [".7z"] = Icons.FileArchive,    // fa-file-archive
            [".tar"] = Icons.FileArchive,   // fa-file-archive
            [".gz"] = Icons.FileArchive,    // fa-file-archive
        };

        // Font Awesome icons for asset types
        private static readonly Dictionary<string, char> _assetTypeIcons = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase)
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

        public AssetBrowserLayer(AssetManager assetManager, CoroutineScheduler coroutineScheduler)
        {
            _assetManager = assetManager;
            _coroutineScheduler = coroutineScheduler;

            try
            {
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

        public ValueTask OnImGuiRender(VkCommandBuffer vkCommandBuffer)
        {
            RenderMainWindow();
            return ValueTask.CompletedTask;
        }

        private void RenderMainWindow()
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
            ImGui.Begin("Asset Browser", ImGuiWindowFlags.MenuBar);
            ImGui.PopStyleVar();

            DrawMenuBar();
            DrawSearchControls();
            DrawContentArea();
            DrawLoadingModal();
            DrawContextMenus();

            ImGui.End();
        }

        private void DrawSearchControls()
        {
            ImGui.BeginChild("##AssetBrowserControls", new Vector2(0, ImGui.GetFrameHeightWithSpacing() * 2));

            // Breadcrumb navigation
            DrawBreadcrumbNavigation();

            ImGui.SameLine();

            // Search box
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.4f);
            if (ImGui.InputTextWithHint("##SearchAssets", "Search...", ref _searchQuery, 100))
            {
                // Search updated - we could add debouncing here
            }

            ImGui.SameLine();

            // View controls
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.2f);
            ImGui.SliderFloat("##ThumbnailSize", ref _thumbnailSize, 32f, 128f, "Size: %.0f");

            ImGui.SameLine();

            if (ImguiExtensions.IconButton(_gridView ? "\uf00a" : "\uf03a", _gridView ? "List View" : "Grid View"))
            {
                _gridView = !_gridView;
            }

            ImGui.SameLine();

            if (ImguiExtensions.IconButton("\uf021", "Refresh Directory"))
            {
                _needsDirectoryRefresh = true;
            }

            ImGui.EndChild();
        }

        private void DrawBreadcrumbNavigation()
        {
            try
            {
                if (string.IsNullOrEmpty(_basePath) || !Directory.Exists(_basePath))
                {
                    ImGui.Text("Invalid path");
                    return;
                }

                var currentDir = new DirectoryInfo(_basePath);
                var pathParts = new List<string>();
                var tempDir = currentDir;

                // Build path parts
                while (tempDir != null)
                {
                    pathParts.Insert(0, tempDir.Name);
                    tempDir = tempDir.Parent;
                }

                // If we're at the root, make sure we have the root properly
                if (pathParts.Count == 0)
                {
                    pathParts.Add(_basePath);
                }

                // Draw breadcrumbs with home button
                if (ImGui.Button("\uf015")) // fa-home
                {
                    _basePath = Directory.GetCurrentDirectory();
                    _needsDirectoryRefresh = true;
                    _selectedItems.Clear();
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Home Directory");
                }

                // Draw path parts with unique IDs
                string currentPath = "";
                for (int i = 0; i < pathParts.Count; i++)
                {
                    ImGui.SameLine();
                    ImGui.Text("\uf105"); // fa-angle-right
                    ImGui.SameLine();

                    var part = pathParts[i];
                    currentPath = i == 0 ? part : Path.Combine(currentPath, part);
                    var isLast = i == pathParts.Count - 1;

                    if (isLast)
                    {
                        ImGui.Text(part);
                    }
                    else
                    {
                        // Use the full path as ID to ensure uniqueness
                        string buttonId = $"{part}##{currentPath.GetHashCode()}";
                        if (ImGui.Button(buttonId))
                        {
                            _basePath = currentPath;
                            _needsDirectoryRefresh = true;
                            _selectedItems.Clear();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in breadcrumb navigation");
                ImGui.Text("Navigation error");
            }
        }


        private void DrawContentArea()
        {
            float contentHeight = ImGui.GetContentRegionAvail().Y;
            ImGui.BeginChild("##AssetBrowserContent", new Vector2(0, contentHeight), true);

            // Debug info
            ImGui.TextDisabled($"Path: {_basePath}");
            ImGui.SameLine();
            ImGui.TextDisabled($"Items: {_currentDirectoryItems.Count}");
            ImGui.SameLine();
            ImGui.TextDisabled($"Cache: {_itemCache.Count}");

            if (!Directory.Exists(_basePath))
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "Directory does not exist!");
                if (ImGui.Button("Reset to Current Directory"))
                {
                    _basePath = Directory.GetCurrentDirectory();
                    _needsDirectoryRefresh = true;
                }
            }

            // Refresh directory if needed
            if (_needsDirectoryRefresh)
            {
                StartDirectoryRefreshCoroutine();
                _needsDirectoryRefresh = false;
            }

            // Draw file system view
            if (_currentDirectoryItems.Count == 0)
            {
                ImguiExtensions.CenteredText("No items found...");
                if (ImGui.Button("Force Refresh"))
                {
                    _needsDirectoryRefresh = true;
                }
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

        private void DrawGridView()
        {
            float availableWidth = ImGui.GetContentRegionAvail().X;
            float itemWidth = _thumbnailSize + ImGui.GetStyle().ItemSpacing.X;
            int columns = Math.Max(1, (int)(availableWidth / itemWidth));

            // Use child window with horizontal scrolling if needed
            ImGui.BeginChild("##AssetGridContainer", Vector2.Zero, false, ImGuiWindowFlags.HorizontalScrollbar);

            float windowVisibleX = ImGui.GetWindowPos().X;
            float windowWidth = ImGui.GetWindowWidth();

            int itemCount = 0;
            foreach (var item in _currentDirectoryItems)
            {
                if (ShouldFilterItem(item)) continue;
                itemCount++;
            }

            // Calculate optimal grid layout
            if (columns > 1)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 16));

                int currentColumn = 0;
                foreach (var item in _currentDirectoryItems)
                {
                    if (ShouldFilterItem(item)) continue;

                    if (currentColumn == 0)
                    {
                        ImGui.BeginGroup();
                    }

                    ImGui.PushID(item.UniqueId);
                    DrawGridItem(item);
                    ImGui.PopID();

                    currentColumn++;

                    // Move to next row or same line
                    if (currentColumn < columns)
                    {
                        ImGui.SameLine();
                    }
                    else
                    {
                        ImGui.EndGroup();
                        currentColumn = 0;
                    }
                }

                // End group if we have an incomplete row
                if (currentColumn > 0 && currentColumn < columns)
                {
                    ImGui.EndGroup();
                }

                ImGui.PopStyleVar();
            }
            else
            {
                // Single column layout for very small windows
                foreach (var item in _currentDirectoryItems)
                {
                    if (ShouldFilterItem(item)) continue;

                    ImGui.PushID(item.UniqueId);
                    DrawGridItem(item);
                    ImGui.PopID();
                }
            }

            ImGui.EndChild();
        }

        private void DrawListView()
        {
            if (ImGui.BeginTable("AssetList", 4,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable))
            {
                // Header
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 0.6f);
                ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthStretch, 0.2f);
                ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthStretch, 0.1f);
                ImGui.TableSetupColumn("Modified", ImGuiTableColumnFlags.WidthStretch, 0.1f);
                ImGui.TableHeadersRow();

                foreach (var item in _currentDirectoryItems)
                {
                    if (ShouldFilterItem(item)) continue;

                    ImGui.TableNextRow();
                    DrawListItem(item);
                }

                ImGui.EndTable();
            }
        }

        private void DrawGridItem(FileSystemItem item)
        {
            bool isSelected = _selectedItems.Contains(item.Path);

            // Calculate item dimensions
            float iconSize = _thumbnailSize;
            float textHeight = ImGui.GetTextLineHeight() * 2 + ImGui.GetStyle().ItemSpacing.Y; // Allow for 2 lines of text
            float totalHeight = iconSize + textHeight;

            ImGui.BeginGroup();

            // Draw selection background
            if (isSelected)
            {
                var drawList = ImGui.GetWindowDrawList();
                var min = ImGui.GetCursorScreenPos();
                var max = min + new Vector2(iconSize, totalHeight);
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.Header));
            }

            // Draw icon/button
            var buttonColor = isSelected ? ImGui.GetStyle().Colors[(int)ImGuiCol.Header]
                : ImGui.GetStyle().Colors[(int)ImGuiCol.Button];

            ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, buttonColor * 1.1f);

            Vector2 buttonSize = new Vector2(iconSize, iconSize);
            if (item.IsLoading)
            {
                // Loading spinner
                var frame = (int)(ImGui.GetTime() * 8) % 8;
                var spinnerFrames = new[] { "⣾", "⣽", "⣻", "⢿", "⡿", "⣟", "⣯", "⣷" };
                ImGui.Button($"{spinnerFrames[frame]}##{item.UniqueId}", buttonSize);
            }
            else
            {
                string buttonLabel = $"{item.Icon}##{item.UniqueId}";
                if (ImGui.Button(buttonLabel, buttonSize))
                {
                    HandleItemClick(item);

                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        HandleItemDoubleClick(item);
                    }
                }
            }

            ImGui.PopStyleColor(2);

            // Draw name with proper text wrapping and centering
            var displayName = GetDisplayName(item);
            DrawGridItemName(displayName, iconSize);

            ImGui.EndGroup();

            // Handle hover and tooltips
            if (ImGui.IsItemHovered())
            {
                _currentHoveredItem = item.Path;
                _hoverStartTime = ImGui.GetTime();

                // Show tooltip after delay
                if (ImGui.GetTime() - _hoverStartTime > 0.5)
                {
                    DrawItemTooltip(item);
                }
            }
            else if (_currentHoveredItem == item.Path)
            {
                _currentHoveredItem = null;
            }

            // Handle drag and drop
            HandleItemDragDrop(item);
        }
        private void DrawGridItemName(string name, float availableWidth)
        {
            // Calculate text size and determine if we need wrapping
            var textSize = ImGui.CalcTextSize(name);
            float textPadding = 4f; // Small padding on sides

            if (textSize.X <= availableWidth - textPadding * 2)
            {
                // Text fits, center it
                float offset = (availableWidth - textSize.X) * 0.5f;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
                ImGui.TextUnformatted(name);
            }
            else
            {
                ImGui.TextWrapped(name);
            }
        }

        private void DrawListItem(FileSystemItem item)
        {
            // Use unique ID for list items as well
            ImGui.PushID(item.UniqueId);

            // Name column
            ImGui.TableNextColumn();
            bool isSelected = _selectedItems.Contains(item.Path);

            string selectableLabel = item.IsLoading
                ? $"{ImguiExtensions.GetLoadingSpinnerIcon()} {GetDisplayName(item)}"
                : $"{item.Icon} {GetDisplayName(item)}";

            if (ImGui.Selectable(selectableLabel, isSelected,
                ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick))
            {
                HandleItemClick(item);

                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    HandleItemDoubleClick(item);
                }
            }

            // Type column
            ImGui.TableNextColumn();
            if (item.IsDirectory)
            {
                ImGui.Text("Folder");
            }
            else if (item.IsAssetFile)
            {
                ImGui.Text("Asset");
            }
            else
            {
                ImGui.Text(item.FileExtension.ToUpper().TrimStart('.'));
            }

            // Size column
            ImGui.TableNextColumn();
            if (item.IsDirectory)
            {
                ImGui.Text("-");
            }
            else
            {
                ImGui.Text(FormatFileSize(item.FileSize));
            }

            // Modified column
            ImGui.TableNextColumn();
            ImGui.Text(item.LastModified.ToString("yyyy-MM-dd HH:mm"));

            // Handle hover and tooltips
            if (ImGui.IsItemHovered())
            {
                DrawItemTooltip(item);
            }

            ImGui.PopID();
        }

        private void DrawItemName(string name, float availableWidth)
        {
            ImguiExtensions.CenteredTextWrapped(name, availableWidth);
        }

        private void DrawItemTooltip(FileSystemItem item)
        {
            ImGui.BeginTooltip();

            ImGui.PushTextWrapPos(400f);

            if (item.IsDirectory)
            {
                ImGui.Text(item.Name);
                ImGui.Separator();
                ImGui.Text("\uf07b Folder"); // fa-folder
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

                if (item.IsAssetFile && item.Metadata != null)
                {
                    ImGui.Text($"Type: {item.Metadata.Type} Asset");
                    ImGui.Text($"ID: {item.Metadata.ID}");
                }
                else
                {
                    ImGui.Text($"Type: {item.FileExtension.ToUpper().TrimStart('.')} File");
                }

                ImGui.Text($"Size: {FormatFileSize(item.FileSize)}");
                ImGui.Text($"Modified: {item.LastModified:yyyy-MM-dd HH:mm:ss}");
            }

            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        private void StartDirectoryRefreshCoroutine()
        {
            // Cancel any existing refresh coroutine
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


            // Load directories
            directories = Directory.GetDirectories(_basePath);
            _logger.Debug("Found {Count} directories in {Path}", directories.Length, _basePath);

            foreach (var dir in directories)
            {
                var dirInfo = new DirectoryInfo(dir);
                var item = CreateDirectoryItem(dirInfo);
                _currentDirectoryItems.Add(item);
            }

            yield return new WaitForNextFrame();

            // Load files
            allFiles = Directory.GetFiles(_basePath);
            _logger.Debug("Found {Count} files in {Path}", allFiles.Length, _basePath);

            int filesProcessed = 0;

            foreach (var file in allFiles)
            {
                var fileInfo = new FileInfo(file);
                var relativePath = GetRelativePath(file);

                // Use the file's unique path for cache key
                string cacheKey = fileInfo.FullName;
                if (!_itemCache.TryGetValue(cacheKey, out var item))
                {
                    item = CreateFileItem(fileInfo);
                    _itemCache[cacheKey] = item;

                    // Start metadata loading for asset files
                    if (item.IsAssetFile)
                    {
                        StartAssetMetadataCoroutine(item);
                    }
                }

                _currentDirectoryItems.Add(item);
                filesProcessed++;

                // Yield every 20 files to keep UI responsive
                if (filesProcessed % 20 == 0)
                    yield return new WaitForNextFrame();
            }

            // Sort items: directories first, then files alphabetically
            _currentDirectoryItems.Sort((a, b) =>
            {
                if (a.IsDirectory && !b.IsDirectory) return -1;
                if (!a.IsDirectory && b.IsDirectory) return 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            _logger.Debug("Directory refresh completed. Found {DirCount} directories, {FileCount} files",
                directories.Length, allFiles.Length);
        }



        private FileSystemItem CreateDirectoryItem(DirectoryInfo dirInfo)
        {
            return new FileSystemItem
            {
                Path = dirInfo.FullName,
                Name = dirInfo.Name,
                DisplayName = dirInfo.Name,
                Icon = Icons.Folder, // fa-folder
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
            var isAssetFile = extension == AssetPath.Empty.Extension;

            return new FileSystemItem
            {
                Path = fileInfo.FullName,
                Name = fileInfo.Name,
                DisplayName = Path.GetFileNameWithoutExtension(fileInfo.Name),
                Icon = GetFileIcon(extension),
                IsDirectory = false,
                IsAssetFile = isAssetFile,
                IsLoaded = !isAssetFile, // Non-asset files are immediately "loaded"
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
                : '\uf15b'; // fa-file
        }

        private void StartAssetMetadataCoroutine(FileSystemItem item)
        {
            var coroutineKey = $"metadata_{item.UniqueId}";

            if (_activeCoroutines.TryGetValue(coroutineKey, out var existingCoroutine))
            {
                _coroutineScheduler.StopCoroutine(existingCoroutine);
            }

            item.IsLoading = true;
            item.LoadTimer = Stopwatch.StartNew();

            var metadataCoroutine = _coroutineScheduler.StartCoroutine(
                LoadAssetMetadataCoroutine(item),
                $"LoadMetadata_{item.UniqueId}"
            );

            _activeCoroutines[coroutineKey] = metadataCoroutine;

            metadataCoroutine.OnCompleted += (coroutine) =>
            {
                _activeCoroutines.Remove(coroutineKey);
                _logger.Debug("Metadata loaded for: {Asset}", item.Name);
            };

            metadataCoroutine.OnError += (coroutine, error) =>
            {
                _activeCoroutines.Remove(coroutineKey);
                item.Icon = Icons.Times; // fa-times
                item.IsLoading = false;
                _logger.Warn("Failed to load metadata for {Asset}: {Error}", item.Name, error.Message);
            };
        }

        private IEnumerator LoadAssetMetadataCoroutine(FileSystemItem item)
        {
            _logger.Debug("Starting metadata coroutine for: {Asset}", item.Name);

            var metadataTask = _assetManager.LoadAssetAsync<IAsset>(item.Path);
            yield return new WaitForTask(metadataTask);

            if (metadataTask.IsCompletedSuccessfully)
            {
                var metadata = metadataTask.Result;
                item.Metadata = new AssetMetadataInfo(metadata.ID, metadata.Name, metadata.GetType().Name.Replace("Asset", ""), metadata.Path.ToString());

                // Update icon based on asset type
                item.Icon = GetAssetTypeIcon(item.Metadata.Type);
                item.IsLoaded = true;
                item.IsLoading = false;

                _logger.Debug("Metadata loaded successfully for: {Asset} ({Type})",
                    item.Name, item.Metadata.Type);
            }
            else
            {
                item.Icon = Icons.Times; // fa-times
                item.IsLoading = false;
                throw new Exception($"Failed to load metadata for {item.Name}", metadataTask.Exception);
            }
        }

        private char GetAssetTypeIcon(string assetType)
        {
            return _assetTypeIcons.TryGetValue(assetType?.ToLower() ?? "", out var icon)
                ? icon
                : Icons.Cube; // fa-cube (default)
        }

        private void HandleItemClick(FileSystemItem item)
        {
            if (ImGui.GetIO().KeyCtrl)
            {
                // Toggle selection
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
            else if (item.IsAssetFile && item.Metadata?.Type == "Scene")
            {
                StartSceneLoadingCoroutine(item.Path, item.Name);
            }
            else
            {
                // Open file with default system application
                OpenFileWithDefaultApplication(item.Path);
            }
        }

        private void HandleItemDragDrop(FileSystemItem item)
        {
            // Implement drag and drop functionality here
            // This would handle dragging assets into the scene, between folders, etc.
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
                    _loadingProgress = 0.9f;

                    yield return new WaitForNextFrame();
                    var sceneInitTask =  sceneAsset.InstantiateEntities();

                    yield return new WaitForTask(sceneInitTask);
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

        private bool ShouldFilterItem(FileSystemItem item)
        {
            if (string.IsNullOrEmpty(_searchQuery)) return false;

            var searchText = _searchQuery.ToLowerInvariant();
            var itemName = item.Name.ToLowerInvariant();

            return !itemName.Contains(searchText);
        }

        private string GetRelativePath(string fullPath)
        {
            try
            {
                return Path.GetRelativePath(_basePath, fullPath);
            }
            catch
            {
                return fullPath;
            }
        }

        private string GetDisplayName(FileSystemItem item)
        {
            if (item.IsDirectory)
                return item.Name;

            return _showFileExtensions
                ? item.Name
                : item.DisplayName;
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double len = bytes;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
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
        }

        private void DrawMenuBar()
        {
            if (ImGui.BeginMenuBar())
            {
                if (ImGui.BeginMenu("File"))
                {
                    if (ImGui.MenuItem("\uf65e New Folder")) // fa-folder-plus
                    {
                        CreateNewFolder();
                    }
                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("View"))
                {
                    ImGui.MenuItem("\uf15c Show File Extensions", "", ref _showFileExtensions); // fa-file-alt
                    ImGui.MenuItem("\uf00a Grid View", "", ref _gridView); // fa-th
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

        private float GetEstimatedProgress(Task task)
        {
            if (task.IsCompleted) return 1.0f;
            return Math.Min(0.95f, DateTime.Now.Ticks % 1000000 / 1000000f);
        }

        public void OnUpdate()
        {
            // Coroutines are processed by the Application's CoroutineScheduler
        }

        public void OnRender(VkCommandBuffer vkCommandBuffer)
        {
            // 3D rendering logic would go here if needed
        }

        public Task OnAttach()
        {
            _logger.Info("AssetBrowserLayer attached");

          

            // Subscribe to project events
            _assetManager.OnProjectLoaded += OnProjectLoaded;
            _assetManager.OnAssetRegistered += OnAssetRegistered;

            // Initial refresh
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
            _assetManager.OnAssetRegistered -= OnAssetRegistered;
        }

        private void OnProjectLoaded(ProjectAsset project)
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

        private void OnAssetRegistered(IAsset asset)
        {
            _logger.Debug("Asset registered: {AssetName} ({AssetType})", asset.Name, asset.GetType().Name);

            var assetPath = asset.Path.ToString();
            if (_itemCache.TryGetValue(assetPath, out var cachedItem))
            {
                cachedItem.Metadata = new AssetMetadataInfo(asset.ID, asset.Name, asset.GetType().Name.Replace("Asset", ""), assetPath);
                cachedItem.Icon = GetAssetTypeIcon(cachedItem.Metadata.Type);
                cachedItem.IsLoaded = true;
                cachedItem.IsLoading = false;
            }
        }
    }
}