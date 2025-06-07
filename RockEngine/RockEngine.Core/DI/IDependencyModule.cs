using SimpleInjector;

namespace RockEngine.Core.DI
{
    public interface IDependencyModule
    {
        void RegisterDependencies(Container container);
    }
}
