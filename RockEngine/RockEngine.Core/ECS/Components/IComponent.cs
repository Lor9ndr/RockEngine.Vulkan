
using RockEngine.Core.Rendering;

namespace RockEngine.Core.ECS.Components
{
    public interface IComponent
    {
        public Entity Entity { get; }
        public ValueTask Update(Renderer renderer);
        public ValueTask OnStart(Renderer renderer);
        public void SetEntity(Entity entity);
        void Destroy();
    }
}
