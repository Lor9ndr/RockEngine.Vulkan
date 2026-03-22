
using Microsoft.Win32;
using NUnit.Framework;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Vulkan;
using RockEngine.Vulkan.DeviceFeatures;
using SimpleInjector;
using SimpleInjector.Lifestyles;

namespace RockEngine.Tests
{
    [SetUpFixture]
    public class GlobalTestSetup
    {
        public static Container Container { get; private set; }
        public static VulkanContext VulkanContext { get; private set; }
        public static Scope Scope { get; private set; }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // 1. Create the container once
            Container = IoC.Container;
            Container.Options.AllowOverridingRegistrations = true;
            Container.Options.DefaultScopedLifestyle = new AsyncScopedLifestyle();
            Container.Options.EnableAutoVerification = false;

            // 2. Initialise IoC and register common dependencies
            IoC.Initialize(Container);

            // 3. Create VulkanContext (headless)
            var settings = new AppSettings
            {
                Name = "TestRun",
                EnableValidationLayers = true,
                MaxFramesPerFlight = 3
            };
            var featureRegistry = new FeatureRegistry();
            featureRegistry.RequestFeature(new SamplerAnisotropyFeature() { IsRequired = true });
            featureRegistry.RequestFeature(new DepthClampFeature() { IsRequired = true });
            featureRegistry.RequestFeature(new MultiDrawIndirectFeature() { IsRequired = true });
            featureRegistry.RequestFeature(new ImageCubeArrayFeature() { IsRequired = true });
            featureRegistry.RequestFeature(new GeometryShaderFeature() { IsRequired = true });
            featureRegistry.RequestFeature(new DrawIndirectFirstInstanceFeature() { IsRequired = true });
            featureRegistry.RequestFeature(new PipelineStatisticsQueryFeature() { IsRequired = true });

            // Vulkan 1.1/1.2/1.3 features (required)
            featureRegistry.RequestFeature(new ShaderDrawParametersFeature() { IsRequired = true });
            featureRegistry.RequestFeature(new HostQueryResetFeature() { IsRequired = true });
            featureRegistry.RequestFeature(new ScalarBlockLayoutFeature() { IsRequired = true });
            featureRegistry.RequestFeature(new Synchronization2Feature() { IsRequired = true });

            VulkanContext = new VulkanContext(null, settings, featureRegistry);
            Container.RegisterInstance(VulkanContext);
            Scope = AsyncScopedLifestyle.BeginScope(GlobalTestSetup.Container);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            VulkanContext?.Dispose();
            Scope.Dispose();
            Container?.Dispose();
        }
    }
}