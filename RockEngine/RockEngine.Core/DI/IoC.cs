using RockEngine.Core.Rendering;

using Silk.NET.Windowing;

using SimpleInjector;
using SimpleInjector.Lifestyles;

namespace RockEngine.Core.DI
{
    public static class IoC
    {
        public static Container Container = new Container();
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
        public static void Initialize(Container container)
        {
            if (_isInitialized)
            {
                return;
            }
            Container = container;
            container.Options.ConstructorResolutionBehavior = new GreediestConstructorBehavior();

            container.Options.DefaultScopedLifestyle = new AsyncScopedLifestyle();
            container.Options.EnableAutoVerification = false;
            container.Options.ResolveUnregisteredConcreteTypes = true;
            container.Options.DefaultLifestyle = Lifestyle.Scoped;
            container.Collection.Register<ILayer>(Array.Empty<Type>());

            // Then register modules
            DependencyRegistrator.RegisterModules(container);

            // Verify container for configuration errors
            /*#if DEBUG
                        Container.Verify();
            #endif*/

            _isInitialized = true;
        }
    }
}