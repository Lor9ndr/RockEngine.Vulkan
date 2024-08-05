using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.Rendering.ComponentRenderers.Factories;
using RockEngine.Vulkan.Rendering.ComponentRenderers;

using SimpleInjector;
using SimpleInjector.Lifestyles;
using RockEngine.Vulkan.EventSystem;
using SimpleInjector.Diagnostics;

namespace RockEngine.Vulkan.DI
{
    internal static class IoC
    {
        public static readonly Container Container = new Container();

        public static void Register()
        {
            // Set the default scoped lifestyle
            Container.Options.DefaultScopedLifestyle = new AsyncScopedLifestyle();
            Container.Options.EnableAutoVerification = false;


            // Register component renderers with appropriate lifestyles
            Container.RegisterSingleton<IComponentRenderer<Camera>, CameraRenderer>();
            Container.RegisterSingleton<IComponentRenderer<DebugCamera>, CameraRenderer>();
            Container.RegisterSingleton<IComponentRenderer<TransformComponent>, TransformComponentRenderer>();
            Container.RegisterSingleton<IComponentRenderer<MaterialComponent>, MaterialRenderer>();

            // Register the factory
            Container.RegisterSingleton<MeshComponentRendererFactory>();

            // Register the delegate for creating MeshComponentRenderer with a parameter
            Container.Register<Func<MeshComponent, MeshComponentRenderer>>(() => component =>
            {
                var context = Container.GetRenderingContext();
                // Resolve dependencies and create the instance manually
                var renderer = new MeshComponentRenderer(component, context);
                // Initialize or set properties if needed
                return renderer;
            });

            // Register other dependencies
            Container.RegisterSingleton<IEventSystem, EventSystem.EventSystem>();

            Container.Register<TransformComponent>();
            Container.Register<MeshComponent>();
            Container.Register<MaterialComponent>();
            Container.Register<Camera>();
            Container.Register<DebugCamera>();

        }
    }
}
