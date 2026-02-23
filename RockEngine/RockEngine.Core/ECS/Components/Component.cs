using MessagePack;

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
        [IgnoreMember]
        public Entity Entity { get; private set; }

        [Key(6)]
        public bool IsActive {get; protected set;} = true;

        public virtual void SetEntity(Entity entity)
        {
            Entity = entity;
        }

        public virtual ValueTask OnStart(WorldRenderer renderer)
        {
            return default;
        }

        public virtual ValueTask Update(WorldRenderer renderer)
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
