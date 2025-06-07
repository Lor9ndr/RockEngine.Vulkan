using RockEngine.Core.ECS;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Core.Rendering;
using RockEngine.Vulkan;
using Silk.NET.Windowing;

using SimpleInjector;
using Silk.NET.Input;

namespace RockEngine.Core.DI
{
    public class CoreModule : IDependencyModule
    {
        public void RegisterDependencies(Container container)
        {
            // Core systems
            container.Register<World>(Lifestyle.Scoped);
            container.Register<ILayerStack,LayerStack>(Lifestyle.Scoped);
            container.Register<TextureStreamer>(Lifestyle.Scoped);

            // Singleton services
            container.RegisterSingleton<AssimpLoader>();
            container.RegisterSingleton<PerformanceTracer>();

            // Window-dependent registrations (factory pattern)
            container.Register<VulkanContext>(Lifestyle.Scoped);
            container.Register<GraphicsEngine>(Lifestyle.Scoped);
            container.Register<PipelineManager>(Lifestyle.Scoped);
            container.Register<Renderer>(Lifestyle.Scoped);
            container.Register<InputManager>(Lifestyle.Scoped);
            IoC.Container.RegisterInitializer<LayerStack>(async s =>
            {
                foreach (var item in IoC.Container.GetAllInstances<ILayer>())
                {
                    await s.PushLayer(item);
                }
            });
            IoC.Container.RegisterInitializer<InputManager>(s =>
            {
                var inputContext = IoC.Container.GetInstance<IInputContext>();
                s.Context = inputContext;
            });

            // Factory for IWindow
            container.RegisterInstance<IWindow>(container.IsVerifying
                ? null!
                : Window.Create(WindowOptions.DefaultVulkan));
            container.Register<IInputContext>(() => container.GetInstance<IWindow>().CreateInput(), Lifestyle.Scoped);



        }
    }
}
