using RockEngine.Core.Attributes;
using RockEngine.Core.Rendering;

namespace RockEngine.Core.ECS.Components
{
    public abstract class Component : IComponent
    {
        protected Component()
        {
        }

        [SerializeIgnore]
        public Entity Entity { get; private set; }

        public bool IsActive {get; protected set;}

        public virtual void SetEntity(Entity entity)
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

        public virtual void SetActive(bool isActive = true)
        {
        }
    }
}
