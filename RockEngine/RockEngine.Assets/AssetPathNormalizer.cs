namespace RockEngine.Assets
{
    public static class AssetPathNormalizer
    {
        public static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
    }
}
