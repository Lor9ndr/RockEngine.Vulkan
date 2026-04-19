namespace RockEngine.Assets
{
    public class AssetInfo
    {
        public required string Path { get; set; }
        public required string Name { get; set; }
        public required string Type { get; set; }
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
    }
}