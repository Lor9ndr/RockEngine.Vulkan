using RockEngine.Core.Rendering.Managers;

using SimpleInjector;
using SimpleInjector.Lifestyles;

namespace RockEngine.Core.DI
{
    internal static class IoC
    {
        public static readonly Container Container = new Container();

        public static void Register()
        {
            // Set the default scoped lifestyle
            Container.Options.DefaultScopedLifestyle = new AsyncScopedLifestyle();
            Container.Options.EnableAutoVerification = false;


            // Register other dependencies
            Container.RegisterSingleton<AssimpLoader>();
            Container.RegisterSingleton<PipelineManager>();

        }
    }
}
