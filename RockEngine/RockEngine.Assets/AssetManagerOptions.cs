namespace RockEngine.Assets
{
    public class AssetManagerOptions
    {
        public bool EnableAssetCaching { get; set; } = true;
        public TimeSpan CacheDuration { get; set; } = TimeSpan.FromHours(1);
        public int MaxCacheSize { get; set; } = 1024 * 1024 * 128; // 128 MB
        public int MaxConcurrentLoads { get; set; } = 8;
    }
}