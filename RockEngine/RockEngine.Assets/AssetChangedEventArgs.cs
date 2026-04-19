using RockEngine.Assets;

namespace RockEngine.Core.Assets
{
    public class AssetChangedEventArgs : EventArgs
    {
        public required IAsset Asset { get; set; }
        public AssetChangeType ChangeType { get; set; }
        public required string Path { get; set; }
    }
}