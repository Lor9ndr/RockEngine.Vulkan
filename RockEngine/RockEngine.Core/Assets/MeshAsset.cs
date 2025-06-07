using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.ECS.Components;

namespace RockEngine.Core.Assets
{
    public class MeshAsset : Asset, ISerializableAsset<MeshData>
    {
        private readonly AssetManager _assetManager;
        private MeshData _data;

        public MeshAsset(AssetManager assetManager, string path, MeshData data)
            :base(path)
        {
            _assetManager = assetManager;
            _data = data;
        }
        public MeshData GetData() => _data;

        public void UpdateData(MeshData data)
        {
            _data = data;
            // Handle mesh reloading if needed
        }

        public override async Task LoadAsync()
        {
            if (IsLoaded) return;

            IsLoaded = true;
        }

        public override void Unload()
        {
           
            IsLoaded = false;
        }
    }
}
