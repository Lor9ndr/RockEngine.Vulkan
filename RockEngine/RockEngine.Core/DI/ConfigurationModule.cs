using RockEngine.DI;
using RockEngine.Vulkan;

using SimpleInjector;



namespace RockEngine.Core.DI
{
    public class ConfigurationModule : IDependencyModule
    {
        public void RegisterDependencies(Container container)
        {
            container.Register<AppSettings>(() =>
            {
                var cfg = ConfigLoader.LoadConfigAsync(container).GetAwaiter().GetResult();
                return cfg;
            }, Lifestyle.Singleton);
        }
    }
}
