using SimpleInjector;

namespace RockEngine.Core.DI
{
    public static class DependencyRegistrator
    {
        public static void RegisterModules(Container container)
        {
            // Get all loaded assemblies
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(asm => !asm.IsDynamic)
                .ToArray();

            // Find and execute all dependency modules
            foreach (var assembly in assemblies)
            {
                var moduleTypes = assembly.GetExportedTypes()
                    .Where(t => !t.IsAbstract &&
                                !t.IsInterface &&
                                typeof(IDependencyModule).IsAssignableFrom(t));

                foreach (var type in moduleTypes)
                {
                    var module = (IDependencyModule)Activator.CreateInstance(type);
                    module.RegisterDependencies(container);
                }
            }
            ContainerExtensions.BuildRenderPassSystem(container);
        }
    }
}
