namespace RockEngine.Core.Assets
{
    public struct AssetPath : IEquatable<AssetPath>
    {
        public string Folder { get; set; }
        public string Name { get;  set;}
        public string Extension { get; set; }

        public AssetPath(string folder, string name, string extension = ".asset")
        {
            Folder = NormalizeFolder(folder);
            Name = name;
            Extension = extension;
        }

        public AssetPath(string fullPath)
        {
            // Normalize and parse the path
            fullPath = fullPath.Replace('\\', '/').Trim('/');

            // Extract extension
            var lastDot = fullPath.LastIndexOf('.');
            if (lastDot > 0)
            {
                Extension = fullPath.Substring(lastDot);
                fullPath = fullPath.Substring(0, lastDot);
            }
            else
            {
                Extension = ".asset";
            }

            // Extract folder and name
            var lastSlash = fullPath.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                Folder = fullPath.Substring(0, lastSlash);
                Name = fullPath.Substring(lastSlash + 1);
            }
            else
            {
                Folder = string.Empty;
                Name = fullPath;
            }
        }

        public readonly string FullPath => $"{Folder}\\{Name}{Extension}";
        public readonly string RelativePath => $"{Folder}\\{Name}";

        public static AssetPath Empty => new AssetPath();

        public readonly bool Equals(AssetPath other) =>
            Folder == other.Folder &&
            Name == other.Name &&
            Extension == other.Extension;

        public override bool Equals(object obj) =>
            obj is AssetPath other && Equals(other);

        public override readonly int GetHashCode() =>
            HashCode.Combine(Folder, Name, Extension);

        public override readonly string ToString() => FullPath;

        public static bool operator ==(AssetPath left, AssetPath right) =>
            left.Equals(right);

        public static bool operator !=(AssetPath left, AssetPath right) =>
            !left.Equals(right);

        // Implicit conversion from string
        public static implicit operator AssetPath(string path) => new AssetPath(path);

        private static string NormalizeFolder(string folder)
        {
            return folder?.Replace('\\', '/').Trim('/') ?? string.Empty;
        }
    }
}