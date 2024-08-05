using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.Rendering.ComponentRenderers;
using RockEngine.Vulkan.Rendering.ComponentRenderers.Factories;
using RockEngine.Vulkan.VulkanInitilizers;

using SimpleInjector;

namespace RockEngine.Vulkan.DI
{
    public static class SimpleInjectorExtensions
    {
        public static IComponentRenderer<T> GetRenderer<T>(this Container container) where T : Component
        {
            return container.GetInstance<IComponentRenderer<T>>();
        }

        public static object GetRenderer(this Container container, Type componentType)
        {
            var rendererType = typeof(IComponentRenderer<>).MakeGenericType(componentType);
            return container.GetInstance(rendererType);
        }
        public static IComponentRendererFactory<TRenderer, TComponent> GetFactory<TRenderer, TComponent>(this Container container) where TRenderer : IComponentRenderer<TComponent> where TComponent : Component
        {
            return container.GetInstance<IComponentRendererFactory<TRenderer, TComponent>>();
        }

        public static VulkanContext GetRenderingContext(this Container container) 
        {
            return container.GetInstance<VulkanContext>();
        }

    }
}
