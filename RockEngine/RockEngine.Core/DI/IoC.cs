using RockEngine.Core.Rendering;

using Silk.NET.Windowing;

using SimpleInjector;
using SimpleInjector.Lifestyles;

    


namespace RockEngine.Core.DI
{
    public static class IoC
    {
        public static readonly Container Container = new Container();
        private static bool _isInitialized = false;

        public static void Initialize(Application application)
        {
            if (_isInitialized)
            {
                return;
            }
            Window.PrioritizeGlfw();

            Container.Options.ConstructorResolutionBehavior = new GreediestConstructorBehavior();

            Container.Options.DefaultScopedLifestyle = new AsyncScopedLifestyle();
            Container.Options.EnableAutoVerification = false;
            Container.Options.ResolveUnregisteredConcreteTypes = true;
            Container.Options.DefaultLifestyle = Lifestyle.Scoped;
            Container.Collection.Register<ILayer>(Array.Empty<Type>());
            Container.RegisterInstance(application);

            // Then register modules
            DependencyRegistrator.RegisterModules(Container);

            // Verify container for configuration errors
/*#if DEBUG
            Container.Verify();
#endif*/

            _isInitialized = true;
        }
    }
}