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
using RockEngine.Vulkan;

using System.Collections;
using System.Diagnostics;
using System.Numerics;

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
        private bool _isLoadingScene = false;
        private string _loadingSceneName = "";
        private float _loadingProgress = 0f;

        // Visual State
        private readonly Dictionary<string, Vector4> _folderColors = new();
        private string _currentHoveredItem = null;
        private double _hoverStartTime = 0;

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
                // Search updated
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
            ImGui.BeginChild("##AssetBrowserContent", new Vector2(0, contentHeight));

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

            ImGui.BeginChild("##AssetGridContainer",
                Vector2.Zero,
                ImGuiChildFlags.None,
                ImGuiWindowFlags.AlwaysVerticalScrollbar);

            int itemsDrawn = 0;
            int columnCount = 0;

            foreach (var item in _currentDirectoryItems)
            {
                if (ShouldFilterItem(item)) continue;

                if (columns > 1)
                {
                    if (columnCount > 0 && columnCount < columns)
                    {
                        ImGui.SameLine();
                    }

                    ImGui.PushID(item.UniqueId);
                    ImGui.BeginGroup();
                    DrawGridItem(item);
                    ImGui.EndGroup();
                    ImGui.PopID();

                    columnCount++;
                    itemsDrawn++;

                    if (columnCount >= columns)
                    {
                        columnCount = 0;
                    }
                }
                else
                {
                    ImGui.PushID(item.UniqueId);
                    DrawGridItem(item);
                    ImGui.PopID();
                    itemsDrawn++;
                }
            }

            ImGui.EndChild();
        }

        private void DrawListView()
        {
            if (ImGui.BeginTable("AssetList", 4,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable))
            {
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Modified", ImGuiTableColumnFlags.WidthStretch);
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

            float iconSize = _thumbnailSize + ImGui.GetStyle().ItemSpacing.X;
            float textHeight = ImGui.GetTextLineHeight() * 2f + ImGui.GetStyle().ItemSpacing.Y;
            float totalHeight = iconSize + textHeight;

            ImGui.BeginGroup();

            if (isSelected)
            {
                var drawList = ImGui.GetWindowDrawList();
                var min = ImGui.GetCursorScreenPos();
                var max = min + new Vector2(iconSize, totalHeight);
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.Header));
            }

            Vector2 buttonSize = new(iconSize, iconSize);
            if (item.IsLoading)
            {
                var frame = (int)(ImGui.GetTime() * 8) % 8;
                ImguiExtensions.Spinner(new Vector2(0), 10, 1, 1);
            }
            else
            {
                string buttonLabel = $"{item.Icon}##{item.UniqueId}";
                ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
                ImGui.SetNextWindowContentSize(buttonSize);
                ImGui.AlignTextToFramePadding();
                if(item.IsAssetFile && item.AssetHeader.AssetType == typeof(TextureAsset))
                {
                    var asset = _assetManager.GetAssetAsync<TextureAsset>(item.AssetHeader.AssetId).GetAwaiter().GetResult();
                    var thumbnail = _thumbnailService.GetOrCreateThumbnailAsync(asset).GetAwaiter().GetResult();
                    if(ImGui.ImageButton($"{asset.ID}", _imGuiController.GetTextureID(thumbnail.Texture), buttonSize))
                    {
                        HandleItemClick(item);

                        if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                        {
                            HandleItemDoubleClick(item);
                        }
                    }
                }
                else
                {
                    if (ImGui.Selectable(buttonLabel, isSelected, ImGuiSelectableFlags.AllowDoubleClick, buttonSize))
                    {
                        HandleItemClick(item);

                        if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                        {
                            HandleItemDoubleClick(item);
                        }
                    }
                }
             

                ImGui.PopStyleVar();
            }

            var displayName = GetDisplayName(item);
            ImGui.AlignTextToFramePadding();
            DrawGridItemName(displayName, iconSize);

            ImGui.EndGroup();

            if (ImGui.IsItemHovered())
            {
                _currentHoveredItem = item.Path;
                _hoverStartTime = ImGui.GetTime();

                if (ImGui.GetTime() - _hoverStartTime > 0.5)
                {
                    DrawItemTooltip(item);
                }
            }
            else if (_currentHoveredItem == item.Path)
            {
                _currentHoveredItem = null;
            }

            HandleItemDragDrop(item);
        }

        private void DrawGridItemName(string name, float availableWidth)
        {
            var textSize = ImGui.CalcTextSize(name);
            float textPadding = 4f;
            float offset = (availableWidth - textSize.X) * 0.5f;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
            ImGui.Text(name);
        }

        private void DrawListItem(FileSystemItem item)
        {
            ImGui.PushID(item.UniqueId);

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

            ImGui.TableNextColumn();
            if (item.IsDirectory)
            {
                ImGui.Text("-");
            }
            else
            {
                ImGui.Text(FormatFileSize(item.FileSize));
            }

            ImGui.TableNextColumn();
            ImGui.Text(item.LastModified.ToString("yyyy-MM-dd HH:mm"));

            if (ImGui.IsItemHovered())
            {
                DrawItemTooltip(item);
            }

            ImGui.PopID();
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

                ImGui.Text($"Size: {FormatFileSize(item.FileSize)}");
                ImGui.Text($"Modified: {item.LastModified:yyyy-MM-dd HH:mm:ss}");
            }

            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
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
                typeName = typeName.Substring(0, commaIndex);
            }

            var lastDot = typeName.LastIndexOf('.');
            if (lastDot > 0)
            {
                typeName = typeName.Substring(lastDot + 1);
            }

            if (typeName.EndsWith("Asset", StringComparison.OrdinalIgnoreCase))
            {
                typeName = typeName.Substring(0, typeName.Length - 5);
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
                    _loadingProgress = 0.9f;

                    yield return new WaitForNextFrame();
                    var sceneInitTask = sceneAsset.InstantiateEntities();

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
        private float GetEstimatedProgress(Task task)
        {
            if (task.IsCompleted) return 1.0f;
            return Math.Min(0.95f, DateTime.Now.Ticks % 1000000 / 1000000f);
        }


        private void HandleItemDragDrop(FileSystemItem item)
        {
            // Implement drag and drop if needed
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