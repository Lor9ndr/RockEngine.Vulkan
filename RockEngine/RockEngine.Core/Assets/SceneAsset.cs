using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.ECS;

namespace RockEngine.Core.Assets
{
    public class SceneAsset : Asset, ISerializableAsset<SceneData>
    {
        private readonly World _world;
        private readonly AssetManager _assetManager;
        private SceneData _data;
        private readonly List<Entity> _entities = new();

        public SceneAsset(World world, AssetManager assetManager, string path, SceneData data)
            : base(path)
        {
            _world = world;
            _assetManager = assetManager;
            _data = data;
        }

        public SceneData GetData() => _data;

        public void UpdateData(SceneData data)
        {
            _data = data;
            // Handle scene reloading
        }

        public override async Task LoadAsync()
        {
            if (IsLoaded) return;
/*
            // Load dependencies first
            foreach (var assetId in _data.AssetDependencies)
            {
                await _assetManager.LoadAsync<IAsset>(assetId);
            }

            // Instantiate entities
            foreach (var entityData in _data.Entities)
            {
                var entity = CreateEntity(entityData);
                _entities.Add(entity);
                _world.AddEntity(entity);
            }*/

            IsLoaded = true;
        }

        public override void Unload()
        {
            foreach (var entity in _entities)
            {
                _world.RemoveEntity(entity);
                entity.Destroy();
            }
            _entities.Clear();
            IsLoaded = false;
        }
    }
}
