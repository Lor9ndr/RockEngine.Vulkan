using SimpleInjector;

namespace RockEngine.Core.DI
{

namespace RockEngine.Core.DI.Modules
    {
        public class ConfigurationModule : IDependencyModule
        {
            public void RegisterDependencies(Container container)
            {
                var config = ConfigLoader.LoadConfig();
                container.RegisterInstance(config);
            }
        }
    }
}