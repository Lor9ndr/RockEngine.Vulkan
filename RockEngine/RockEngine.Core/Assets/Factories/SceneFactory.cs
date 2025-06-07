using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.ECS;

namespace RockEngine.Core.Assets.Factories
{
    public class SceneFactory : IAssetFactory<SceneData, SceneAsset>
    {
        private readonly World _world;
        private readonly AssetManager _assetManager;

        public SceneFactory(World world, AssetManager assetManager)
        {
            _world = world;
            _assetManager = assetManager;
        }

        public SceneAsset CreateAsset(string path, SceneData data) => new SceneAsset(_world, _assetManager, path, data);
    }
}
