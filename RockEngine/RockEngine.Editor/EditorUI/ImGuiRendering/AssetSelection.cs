using RockEngine.Assets;

namespace RockEngine.Editor.EditorUI.ImGuiRendering
{
    public static class AssetSelection
    {
        public static event Action<IAsset> OnAssetSelected;
        private static IAsset _selectedAsset;

        public static IAsset SelectedAsset
        {
            get => _selectedAsset;
            set
            {
                if (_selectedAsset != value)
                {
                    _selectedAsset = value;
                    OnAssetSelected?.Invoke(_selectedAsset);
                }
            }
        }
    }
}
