using RockEngine.Core.Rendering;

namespace RockEngine.Core.ECS.Components
{
    public abstract class Component : IComponent
    {
        protected Component()
        {
        }

        public Entity Entity { get; private set; }

        public void SetEntity(Entity entity)
        {
            Entity = entity;
        }

        public virtual ValueTask OnStart(Renderer renderer)
        {
            return default;
        }

        public virtual ValueTask Update(Renderer renderer)
        {
            return default;
        }

        public virtual void Destroy()
        {
        }
    }
}
