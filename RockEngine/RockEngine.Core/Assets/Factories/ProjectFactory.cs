using RockEngine.Core.Assets.AssetData;

namespace RockEngine.Core.Assets.Factories
{
    public class ProjectFactory : IAssetFactory<ProjectData, ProjectAsset>
    {
        private readonly AssetManager _assetManager;

        public ProjectFactory(AssetManager assetManager)
        {
            _assetManager = assetManager;
        }

        public ProjectAsset CreateAsset(string path, ProjectData data) => new ProjectAsset(_assetManager, path, data);
    }
}
