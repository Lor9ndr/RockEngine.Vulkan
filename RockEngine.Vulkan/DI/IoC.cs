using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.Rendering.ComponentRenderers;
using RockEngine.Vulkan.VkObjects;

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
            Container.Options.EnableAutoVerification = false;


            // Register component renderers with appropriate lifestyles
            Container.RegisterSingleton<IComponentRenderer<Camera>, CameraRenderer>();
            Container.RegisterSingleton<IComponentRenderer<DebugCamera>, CameraRenderer>();
            Container.Register<IComponentRenderer<TransformComponent>, TransformComponentRenderer>();
            Container.Register<IComponentRenderer<LightComponent>, LightComponentRenderer>();
            Container.Register<IComponentRenderer<MeshComponent>, MeshComponentRenderer>();

            // Register other dependencies
            Container.RegisterSingleton<AssimpLoader>();
            Container.RegisterSingleton<PipelineManager>();


            // Register components
            Container.Register<TransformComponent>();
            Container.Register<MeshComponent>();
            Container.Register<Camera>();
            Container.Register<DebugCamera>();
            Container.Register<LightComponent>();



        }
    }
}
