using RockEngine.Core.Coroutines;
using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.ECS.Components.Physics;
using RockEngine.Core.Physics;
using RockEngine.Core.Registries;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Vulkan;

using Silk.NET.Input;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl;

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

            // Singleton services
            container.RegisterSingleton<AssimpLoader>();

            // Window-dependent registrations (factory pattern)
            container.Register<VulkanContext>(Lifestyle.Scoped);
            container.Register<GraphicsContext>(Lifestyle.Scoped);
            container.Register<PipelineManager>(Lifestyle.Scoped);
            container.Register<WorldRenderer>(Lifestyle.Scoped);
            container.Register<InputManager>(Lifestyle.Scoped);
            container.Register<IShaderManager, ShaderManager>(Lifestyle.Scoped);
            container.Register<IServiceProvider, Container>(Lifestyle.Singleton);
            container.Register<CoroutineScheduler>(Lifestyle.Singleton);
            container.Register<ShadowManager>(Lifestyle.Scoped);
            container.Register<PhysicsManager>(Lifestyle.Scoped);
            container.Register<IServiceProvider, Container>(Lifestyle.Singleton);


            /*IoC.Container.RegisterInitializer<LayerStack>(async s =>
            {
                foreach (var item in IoC.Container.GetInstance<IEnumerable<ILayer>>())
                {
                    await s.PushLayer(item);
                }
            });*/
           

            // Factory for IWindow
            SdlWindowing.Use();
            container.RegisterInstance<IWindow>(Window.Create(WindowOptions.DefaultVulkan  with
            {
            }
            ));
           
            IoC.Container.RegisterInitializer<InputManager>(s =>
            {
                var window = container.GetInstance<IWindow>();
                s.SetInput(window, window.CreateInput());
            });



            container.RegisterRenderPassStrategy<DeferredPassStrategy>()
                .Before<SwapchainPassStrategy>();
            container.RegisterRenderPassStrategy<ShadowPassStrategy>()
                .Before<DeferredPassStrategy>();

            container.RegisterRenderPassStrategy<SwapchainPassStrategy>()
                .AfterAll();

            container.RegisterRenderSubPass<GeometryPass, DeferredPassStrategy>();
            container.RegisterRenderSubPass<LightingPass, DeferredPassStrategy>();
            container.RegisterRenderSubPass<PostLightPass, DeferredPassStrategy>();

            container.RegisterRenderSubPass<ShadowPass, ShadowPassStrategy>();

            // Components
            container.Register<MeshRenderer>(Lifestyle.Transient);
            container.Register<Light>(Lifestyle.Transient);
            container.Register<Camera>(Lifestyle.Transient);
            container.Register<Skybox>(Lifestyle.Transient);
            container.Register<Transform>(Lifestyle.Transient);
            container.Register<RigidbodyComponent>(Lifestyle.Transient);
            container.Register<SphereColliderComponent>(Lifestyle.Transient);
            container.Register<BoxColliderComponent>(Lifestyle.Transient);
            container.Register<CapsuleColliderComponent>(Lifestyle.Transient);


            //container.RegisterRenderSubPass<ScreenPass, SwapchainPassStrategy>();


            container.Register<GlobalUbo>();

            var poolSizes = new[]
            {
                new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 300),
                new DescriptorPoolSize(DescriptorType.UniformBuffer, 200),
                new DescriptorPoolSize(DescriptorType.StorageBuffer, 200),
                new DescriptorPoolSize(DescriptorType.InputAttachment, 20),
                new DescriptorPoolSize(DescriptorType.UniformBufferDynamic, 20),
                new DescriptorPoolSize(DescriptorType.StorageImage, 10)
            };
            container.Register<DescriptorPoolManager>(() => new DescriptorPoolManager(container.GetInstance<VulkanContext>(), poolSizes, 500), Lifestyle.Scoped);
            container.Register<LightManager>(() => new LightManager(container.GetInstance<VulkanContext>(), (uint)container.GetInstance<VulkanContext>().MaxFramesPerFlight, WorldRenderer.MAX_LIGHTS_SUPPORTED), Lifestyle.Scoped);


            container.Register<TransformManager>(() =>
            {
                var vkContext = container.GetInstance<VulkanContext>();
                return new TransformManager(vkContext, (uint)vkContext.MaxFramesPerFlight);
            }, Lifestyle.Scoped);

            container.Register<CameraManager>(Lifestyle.Scoped);
            container.Register<IndirectCommandManager>(()=>
            {
                var vkContext = container.GetInstance<VulkanContext>();
                
                return new IndirectCommandManager(vkContext, TransformManager.INITIAL_CAPACITY, container.GetInstance<TransformManager>(), container.GetInstance<GlobalGeometryBuffer>());
            },Lifestyle.Scoped);

            container.Register<IRegistry<RckPipeline, string>, PipelineRegistry>(Lifestyle.Scoped);
            container.Register<IRegistry<RckRenderPass, Type>, RenderPassRegistry>(Lifestyle.Scoped);
            container.Register<GlobalGeometryBuffer>(() =>
            {
                var settings = container.GetInstance<AppSettings>();
                var context = container.GetInstance<VulkanContext>();
                return new GlobalGeometryBuffer(context);
            }, Lifestyle.Singleton);


        }
    }
}
