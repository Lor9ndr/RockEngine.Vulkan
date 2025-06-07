using RockEngine.Core.Assets.AssetData;

namespace RockEngine.Core.Assets.Factories
{
    public class MeshFactory : IAssetFactory<MeshData, MeshAsset>
    {
        private readonly AssetManager _assetManager;

        public MeshFactory(AssetManager assetManager)
        {
            _assetManager = assetManager;
        }

        public MeshAsset CreateAsset(string path, MeshData data) => new MeshAsset(_assetManager, path, data);
    }
}
