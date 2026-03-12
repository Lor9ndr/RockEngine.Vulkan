using System.Diagnostics;

namespace RockEngine.Assets
{
    /// <summary>
    /// Represents a validated file path for assets with proper normalization and validation.
    /// </summary>
    [DebuggerDisplay("{FullPath} (Valid: {IsValid})")]
    public readonly struct AssetPath : IEquatable<AssetPath>, IComparable<AssetPath>
    {
        private readonly string _normalizedFolder;
        private readonly string _name = "EmptyAsset";
        private readonly string _extension;

        /// <summary>
        /// Gets the folder path (normalized and platform-independent).
        /// </summary>
        public string Folder => _normalizedFolder ?? string.Empty;

        /// <summary>
        /// Gets the file name without extension.
        /// </summary>
        public string Name => _name ?? string.Empty;

        /// <summary>
        /// Gets the file extension including the dot.
        /// </summary>
        public string Extension => _extension ?? ".asset";

        /// <summary>
        /// Gets the full normalized path.
        /// </summary>
        public string FullPath
        {
            get
            {
                if (string.IsNullOrEmpty(Folder))
                    return $"{Name}{Extension}";
                return $"{Folder}/{Name}{Extension}";
            }
        }

        /// <summary>
        /// Gets the path without extension.
        /// </summary>
        public string RelativePath
        {
            get
            {
                if (string.IsNullOrEmpty(Folder))
                    return Name;
                return $"{Folder}/{Name}";
            }
        }

        /// <summary>
        /// Gets a value indicating whether this asset path is valid.
        /// </summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(Name) &&
                              !string.IsNullOrWhiteSpace(Extension) &&
                              Extension.StartsWith(".") &&
                              !HasInvalidCharacters(Name) &&
                              !HasInvalidCharacters(Folder, allowPathSeparators: true);

        /// <summary>
        /// Gets the platform-specific full path.
        /// </summary>
        public string PlatformFullPath => Path.Combine(GetPlatformFolder(), $"{Name}{Extension}");

        /// <summary>
        /// Represents an empty, invalid asset path.
        /// </summary>
        public static AssetPath Empty => new AssetPath();

        /// <summary>
        /// Initializes a new instance of the AssetPath struct.
        /// </summary>
        /// <param name="folder">The folder path.</param>
        /// <param name="name">The file name without extension.</param>
        /// <param name="extension">The file extension (default: ".asset").</param>
        /// <exception cref="ArgumentException">Thrown when name is null or empty.</exception>
        public AssetPath(string folder, string name, string extension = ".asset")
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("File name cannot be null or empty.", nameof(name));

            _normalizedFolder = NormalizeFolder(folder);
            _name = name.Trim();
            _extension = NormalizeExtension(extension);
        }

        /// <summary>
        /// Initializes a new instance of the AssetPath struct from a full path string.
        /// </summary>
        /// <param name="fullPath">The full path to parse.</param>
        /// <exception cref="ArgumentException">Thrown when fullPath is null or empty.</exception>
        public AssetPath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                throw new ArgumentException("Full path cannot be null or empty.", nameof(fullPath));

            // Normalize and parse the path
            var normalizedPath = fullPath.Replace('\\', '/').Trim('/');

            // Extract extension
            var lastDot = normalizedPath.LastIndexOf('.');
            string extension;
            string pathWithoutExtension;

            if (lastDot > 0 && lastDot > normalizedPath.LastIndexOf('/'))
            {
                extension = normalizedPath[lastDot..];
                pathWithoutExtension = normalizedPath[..lastDot];
            }
            else
            {
                extension = ".asset";
                if (normalizedPath.StartsWith('.'))
                {
                    pathWithoutExtension = string.Empty;
                }
                else
                {
                    pathWithoutExtension = normalizedPath;
                }
            }

            // Extract folder and name
            var lastSlash = pathWithoutExtension.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                _normalizedFolder = pathWithoutExtension[..lastSlash];
                _name = pathWithoutExtension[(lastSlash + 1)..];
            }
            else
            {
                _normalizedFolder = string.Empty;
                _name = pathWithoutExtension;
            }

            _extension = NormalizeExtension(extension);
        }

        /// <summary>
        /// Creates an AssetPath only if the provided parameters form a valid path.
        /// </summary>
        /// <param name="folder">The folder path.</param>
        /// <param name="name">The file name.</param>
        /// <param name="extension">The file extension.</param>
        /// <param name="path">The resulting AssetPath if valid.</param>
        /// <returns>True if the path is valid, false otherwise.</returns>
        public static bool TryCreate(string folder, string name, string extension, out AssetPath path)
        {
            path = Empty;

            if (string.IsNullOrWhiteSpace(name))
                return false;

            try
            {
                path = new AssetPath(folder, name, extension);
                return path.IsValid;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Creates an AssetPath from a full path string if it's valid.
        /// </summary>
        /// <param name="fullPath">The full path string.</param>
        /// <param name="path">The resulting AssetPath if valid.</param>
        /// <returns>True if the path is valid, false otherwise.</returns>
        public static bool TryCreate(string fullPath, out AssetPath path)
        {
            path = Empty;

            if (string.IsNullOrWhiteSpace(fullPath))
                return false;

            try
            {
                path = new AssetPath(fullPath);
                return path.IsValid;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Changes the extension of the current path.
        /// </summary>
        /// <param name="newExtension">The new extension.</param>
        /// <returns>A new AssetPath with the changed extension.</returns>
        public AssetPath ChangeExtension(string newExtension)
        {
            return new AssetPath(Folder, Name, newExtension);
        }

        /// <summary>
        /// Changes the folder of the current path.
        /// </summary>
        /// <param name="newFolder">The new folder.</param>
        /// <returns>A new AssetPath with the changed folder.</returns>
        public AssetPath ChangeFolder(string newFolder)
        {
            return new AssetPath(newFolder, Name, Extension);
        }

        /// <summary>
        /// Changes the name of the current path.
        /// </summary>
        /// <param name="newName">The new name.</param>
        /// <returns>A new AssetPath with the changed name.</returns>
        public AssetPath ChangeName(string newName)
        {
            return new AssetPath(Folder, newName, Extension);
        }

        /// <summary>
        /// Combines the current folder with additional path segments.
        /// </summary>
        /// <param name="paths">Additional path segments to combine.</param>
        /// <returns>A new AssetPath with the combined folder.</returns>
        public AssetPath Combine(params string[] paths)
        {
            if (paths == null || paths.Length == 0)
                return this;

            var combinedFolder = Folder;
            foreach (var path in paths)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    var normalizedPath = path.Replace('\\', '/').Trim('/');
                    if (string.IsNullOrEmpty(combinedFolder))
                        combinedFolder = normalizedPath;
                    else
                        combinedFolder = $"{combinedFolder}/{normalizedPath}";
                }
            }

            return new AssetPath(combinedFolder, Name, Extension);
        }

        public readonly bool Equals(AssetPath other) =>
            Folder == other.Folder &&
            Name == other.Name &&
            Extension == other.Extension;

        public override readonly bool Equals(object obj) =>
            obj is AssetPath other && Equals(other);

        public override readonly int GetHashCode() =>
            HashCode.Combine(Folder, Name, Extension);

        public override readonly string ToString() => FullPath;

        public readonly int CompareTo(AssetPath other)
        {
            var folderComparison = string.Compare(Folder, other.Folder, StringComparison.Ordinal);
            if (folderComparison != 0) return folderComparison;

            var nameComparison = string.Compare(Name, other.Name, StringComparison.Ordinal);
            if (nameComparison != 0) return nameComparison;

            return string.Compare(Extension, other.Extension, StringComparison.Ordinal);
        }

        public static bool operator ==(AssetPath left, AssetPath right) => left.Equals(right);
        public static bool operator !=(AssetPath left, AssetPath right) => !left.Equals(right);

        // Implicit conversion from string
        public static implicit operator AssetPath(string path) => new AssetPath(path);

        private static string NormalizeFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return string.Empty;

            return folder.Replace('\\', '/')
                        .Trim('/')
                        .Trim();
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return ".asset";

            extension = extension.Trim();
            return extension.StartsWith(".") ? extension : $".{extension}";
        }

        private static bool HasInvalidCharacters(string path, bool allowPathSeparators = false)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var invalidChars = Path.GetInvalidFileNameChars();
            if (allowPathSeparators)
            {
                invalidChars = Array.FindAll(invalidChars, c => c != '/' && c != '\\');
            }

            return path.IndexOfAny(invalidChars) >= 0;
        }

        private string GetPlatformFolder()
        {
            return Folder.Replace('/', Path.DirectorySeparatorChar);
        }
    }
}