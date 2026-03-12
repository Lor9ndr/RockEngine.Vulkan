using RockEngine.Assets;

namespace RockEngine.Core.Assets
{
    public class AssetChangedEventArgs : EventArgs
    {
        public IAsset Asset { get; set; }
        public AssetChangeType ChangeType { get; set; }
        public string Path { get; set; }
    }
}