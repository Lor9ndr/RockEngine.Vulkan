using RockEngine.Core.ECS;
using RockEngine.Core.Registries;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Core.Rendering.SubPasses;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Input;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;

using SimpleInjector;

namespace RockEngine.Core.DI
{
    public class CoreModule : IDependencyModule
    {
        public void RegisterDependencies(Container container)
        {
            container.Options.AllowOverridingRegistrations = true;
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
                foreach (var item in IoC.Container.GetInstance<IEnumerable<ILayer>>())
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



            container.RegisterRenderPassStrategy<DeferredPassStrategy>()
                .Before<SwapchainPassStrategy>();
            container.RegisterRenderPassStrategy<SwapchainPassStrategy>()
                .AfterAll();
            container.RegisterRenderSubPass<GeometryPass, DeferredPassStrategy>();
            container.RegisterRenderSubPass<LightingPass, DeferredPassStrategy>();
            container.RegisterRenderSubPass<PostLightPass, DeferredPassStrategy>();

            //container.RegisterRenderSubPass<ScreenPass, SwapchainPassStrategy>();


            container.Register<GlobalUbo>();

            var poolSizes = new[]
            {
                new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 300),
                new DescriptorPoolSize(DescriptorType.UniformBuffer, 100),
                new DescriptorPoolSize(DescriptorType.StorageBuffer, 50),
                new DescriptorPoolSize(DescriptorType.InputAttachment, 10),
                new DescriptorPoolSize(DescriptorType.UniformBufferDynamic, 20),
                new DescriptorPoolSize(DescriptorType.StorageImage, 10)
            };
            container.Register<DescriptorPoolManager>(() => new DescriptorPoolManager(container.GetInstance<VulkanContext>(), poolSizes, 500), Lifestyle.Scoped);
            container.Register<LightManager>(() => new LightManager(container.GetInstance<VulkanContext>(), (uint)container.GetInstance<VulkanContext>().MaxFramesPerFlight, Renderer.MAX_LIGHTS_SUPPORTED), Lifestyle.Scoped);


            container.Register<TransformManager>(() =>
            {
                var vkContext = container.GetInstance<VulkanContext>();
                return new TransformManager(vkContext, (uint)vkContext.MaxFramesPerFlight);
            }, Lifestyle.Scoped);

            container.Register<CameraManager>(Lifestyle.Scoped);
            container.Register<IndirectCommandManager>(()=>
            {
                var vkContext = container.GetInstance<VulkanContext>();
                return new IndirectCommandManager(vkContext, TransformManager.INITIAL_CAPACITY);
            },Lifestyle.Scoped);

            container.Register<IRegistry<VkPipeline, string>, PipelineRegistry>(Lifestyle.Scoped);
            container.Register<IRegistry<EngineRenderPass, Type>, RenderPassRegistry>(Lifestyle.Scoped);


        }
    }
}
