using RockEngine.Core.ECS;

using System.Numerics;

namespace RockEngine.Editor.Selection
{
    public class SelectionContext
    {
        public Entity PrimaryEntity { get; set; }
        public IReadOnlyList<Entity> SelectedEntities { get; set; } = new List<Entity>();
        public SelectionSource Source { get; set; }
        public object AdditionalData { get; set; }
        public Vector2? ScreenPosition { get; set; }
        public Vector3? WorldPosition { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public bool IsMultiSelection => SelectedEntities.Count > 1;
        public bool HasSelection => SelectedEntities.Count > 0;

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

        public bool ContainsEntity(Entity entity)
        {
            return SelectedEntities.Contains(entity);
        }

        public T GetAdditionalData<T>() where T : class
        {
            return AdditionalData as T;
        }

        public bool TryGetAdditionalData<T>(out T data) where T : class
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
    }
}