using RockEngine.Core.Assets.AssetData;

namespace RockEngine.Core.Assets.Factories
{
    public class ProjectAsset : Asset, ISerializableAsset<ProjectData>
    {
        private readonly AssetManager _assetManager;
        private ProjectData _data;

        public ProjectAsset(AssetManager assetManager, string path, ProjectData data)
            : base(path)
        {
            _assetManager = assetManager;
            _data = data;
        }

        public ProjectData GetData() => _data;

        public void UpdateData(ProjectData data)
        {
            _data = data;
            // Handle project updates
        }

        public override async Task LoadAsync()
        {
            if (IsLoaded) return;

           

            IsLoaded = true;
        }

        public override void Unload()
        {
            // Typically projects stay loaded, implement if needed
            IsLoaded = false;
        }
    }
}