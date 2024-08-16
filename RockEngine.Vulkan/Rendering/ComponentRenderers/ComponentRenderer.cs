using RockEngine.Vulkan.ECS;

namespace RockEngine.Vulkan.Rendering.ComponentRenderers
{
    public interface IComponentRenderer<in T> : IDisposable where T : Component
    {
        public ValueTask InitializeAsync(T component);
        public Task RenderAsync(T component, FrameInfo frameInfo);
    }
}
