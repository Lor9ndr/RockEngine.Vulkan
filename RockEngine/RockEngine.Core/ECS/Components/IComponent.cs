
using RockEngine.Core.Rendering;

namespace RockEngine.Core.ECS.Components
{
    public interface IComponent
    {
        public bool IsActive { get; }
        public Entity Entity { get; }
        public ValueTask Update(Renderer renderer);
        public ValueTask OnStart(Renderer renderer);
        public void SetEntity(Entity entity);
        void Destroy();
        void SetActive(bool isActive = true);
    }
}
