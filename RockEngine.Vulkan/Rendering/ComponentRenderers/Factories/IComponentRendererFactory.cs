using RockEngine.Vulkan.ECS;

namespace RockEngine.Vulkan.Rendering.ComponentRenderers.Factories
{
    public interface IComponentRendererFactory<TRenderer, TComponent> where TRenderer:IComponentRenderer<TComponent> where TComponent: Component
    {
        TRenderer Get(TComponent component);
    }
}
