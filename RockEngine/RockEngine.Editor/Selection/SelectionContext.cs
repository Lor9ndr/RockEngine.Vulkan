using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using RockEngine.Assets;
using RockEngine.Core.ECS;

namespace RockEngine.Editor.Selection
{
    public class SelectionContext
    {
        public Entity? PrimaryEntity { get; set; }
        public IReadOnlyList<Entity> SelectedEntities { get; set; } = new List<Entity>();
        public IAsset? SelectedAsset { get; set; }
        public SelectionSource Source { get; set; }
        public object? AdditionalData { get; set; }
        public Vector2? ScreenPosition { get; set; }
        public Vector3? WorldPosition { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public bool IsMultiSelection => SelectedEntities.Count > 1;
        public bool HasEntitySelection => SelectedEntities.Count > 0;
        public bool HasAssetSelection => SelectedAsset != null;
        public bool HasSelection => HasEntitySelection || HasAssetSelection;

        // Constructors unchanged, but we may add one for asset selection
        public SelectionContext() { }

        public SelectionContext(Entity entity, SelectionSource source = SelectionSource.Script)
        {
            PrimaryEntity = entity;
            SelectedEntities = entity != null ? new List<Entity> { entity } : new List<Entity>();
            Source = source;
        }

        public SelectionContext(IEnumerable<Entity> entities, SelectionSource source = SelectionSource.Script)
        {
            var entityList = entities?.ToList() ?? new List<Entity>();
            PrimaryEntity = entityList.FirstOrDefault();
            SelectedEntities = entityList;
            Source = source;
        }

        // Asset selection constructor
        public SelectionContext(IAsset asset, SelectionSource source = SelectionSource.Script)
        {
            SelectedAsset = asset;
            Source = source;
            SelectedEntities = new List<Entity>();
            PrimaryEntity = null;
        }

        public bool ContainsEntity(Entity entity)
        {
            return SelectedEntities.Contains(entity);
        }

        public T? GetAdditionalData<T>() where T : class
        {
            return AdditionalData as T;
        }

        public bool TryGetAdditionalData<T>([NotNullWhen(true)] out  T? data) where T : class
        {
            data = AdditionalData as T;
            return data != null;
        }
    }

    public enum SelectionSource
    {
        SceneHierarchy,
        Viewport,
        ViewportPicking,
        Gizmo,
        Script,
        AssetBrowser
    }
}