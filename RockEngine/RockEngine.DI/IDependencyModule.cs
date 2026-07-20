using SimpleInjector;

namespace RockEngine.DI
{
    public interface IDependencyModule
    {
        void RegisterDependencies(Container container);
    }
}
