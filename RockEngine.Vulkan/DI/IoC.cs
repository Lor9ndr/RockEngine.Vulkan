using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.Rendering.ComponentRenderers;
using RockEngine.Vulkan.Rendering.ComponentRenderers.Factories;

using SimpleInjector;
using SimpleInjector.Lifestyles;

namespace RockEngine.Vulkan.DI
{
    internal static class IoC
    {
        public static readonly Container Container = new Container();

        public static void Register()
        {
            // Set the default scoped lifestyle
            Container.Options.DefaultScopedLifestyle = new AsyncScopedLifestyle();

            // Register component renderers with appropriate lifestyles
            Container.RegisterSingleton<IComponentRenderer<Camera>, CameraRenderer>();
            Container.RegisterSingleton<IComponentRenderer<DebugCamera>, CameraRenderer>();
            Container.RegisterSingleton<IComponentRenderer<Transform>, TransformComponentRenderer>();

            // Register the factory
            Container.RegisterSingleton<MeshComponentRendererFactory>();

            // Register the delegate for creating MeshComponentRenderer with a parameter
            Container.Register<Func<MeshComponent, MeshComponentRenderer>>(() => component =>
            {
                // Resolve dependencies and create the instance manually
                var renderer = new MeshComponentRenderer(component);
                // Initialize or set properties if needed
                return renderer;
            });
        }
    }
}