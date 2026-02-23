using RockEngine.Core.DI;
using RockEngine.Core.Rendering.Passes;

using SimpleInjector;

namespace RockEngine.Core.Diagnostics
{
    public sealed class DiagnosticsModule : IDependencyModule
    {
        public void RegisterDependencies(Container container)
        {
            container.Register<IPipelineStatisticsProvider, DeferredPassStrategy>();
        }
    }
}
