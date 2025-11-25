using RockEngine.Vulkan;

using SimpleInjector;

namespace RockEngine.Core.DI
{

namespace RockEngine.Core.DI.Modules
    {
        public class ConfigurationModule : IDependencyModule
        {
            public void RegisterDependencies(Container container)
            {
                container.Register<AppSettings>(()=>
                {
                    var cfg =  ConfigLoader.LoadConfig(container);
                    return cfg;
                }, Lifestyle.Singleton);
            }
        }
    }
}