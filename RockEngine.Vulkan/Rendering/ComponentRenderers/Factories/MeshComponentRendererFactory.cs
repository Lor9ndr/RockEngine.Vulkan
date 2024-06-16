using RockEngine.Vulkan.ECS;

using SimpleInjector;

namespace RockEngine.Vulkan.Rendering.ComponentRenderers.Factories
{
    internal class MeshComponentRendererFactory 
    {
        private readonly Container _container;

        public MeshComponentRendererFactory(Container container)
        {
            _container = container;
        }

        public MeshComponentRenderer Get(MeshComponent component)
        {
            return _container.GetInstance<Func<MeshComponent, MeshComponentRenderer>>()(component);
        }
    }
}
