using RockEngine.Core.DI;

using SimpleInjector;

namespace RockEngine.Core.Rendering.Materials
{
    public class MaterialModule : IDependencyModule
    {
        public void RegisterDependencies(Container container)
        {
            container.Register<IShaderReflectionProvider, PipelineReflectionProvider>();
            container.Register<IMaterialTemplateFactory,MaterialTemplateFactory>();
            container.Register<ITypeBasedResourceProvider, TypeBasedResourceProvider>();
            container.Register<MaterialTemplateManager>();
        }
    }
}
