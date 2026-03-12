using RockEngine.Assets;

namespace RockEngine.Core.Assets
{
    
    public class ProjectAsset : Asset<ProjectData>, IProject
    {
        private readonly List<Guid> _scenes;

       
        public IReadOnlyList<Guid> Scenes => _scenes;

        public Guid? MainScene { get; set; }

        public DateTime CreatedAt { get; private set; }

        public DateTime LastModified { get; private set; }

        public ProjectAsset()
        {
            _scenes = new List<Guid>();
            CreatedAt = DateTime.UtcNow;
            LastModified = CreatedAt;
        }

        public void AddScene(Guid sceneAssetId)
        {
            if (!_scenes.Contains(sceneAssetId))
            {
                _scenes.Add(sceneAssetId);

                if (_scenes.Count == 1)
                {
                    MainScene = sceneAssetId;
                }

                MarkAsModified();
            }
        }

        public bool RemoveScene(Guid sceneAssetId)
        {
            bool removed = _scenes.Remove(sceneAssetId);
            if (removed)
            {
                if (MainScene == sceneAssetId)
                {
                    MainScene = _scenes.Count > 0 ? _scenes[0] : null;
                }
                MarkAsModified();
            }
            return removed;
        }

        public void SetMainScene(Guid sceneAssetId)
        {
            if (_scenes.Contains(sceneAssetId))
            {
                MainScene = sceneAssetId;
                MarkAsModified();
            }
        }

        public void MarkAsModified()
        {
            LastModified = DateTime.UtcNow;
        }

   
    }
}
