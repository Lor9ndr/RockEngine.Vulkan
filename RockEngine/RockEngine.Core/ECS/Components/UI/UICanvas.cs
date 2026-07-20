using System.Numerics;
using RockEngine.Core.Rendering;

namespace RockEngine.Core.ECS.Components.UI
{
    public class UICanvas : Component
    {
        public Camera? Camera { get; private set; }
        public List<Entity> UIEntities { get; } = new List<Entity>();
        public Vector2 ReferenceResolution { get; set; } = new Vector2(1920, 1080);

        public override async ValueTask OnStart(WorldRenderer renderer)
        {
            Camera = Entity.GetComponent<Camera>() ?? throw new InvalidOperationException("UICanvas must be on an entity with a Camera component.");

            await base.OnStart(renderer).ConfigureAwait(false);
        }

        public void AddUIEntity(Entity entity)
        {
            if (!UIEntities.Contains(entity))
            {
                UIEntities.Add(entity);
            }
        }

        public void RemoveUIEntity(Entity entity)
        {
            UIEntities.Remove(entity);
        }
    }
}