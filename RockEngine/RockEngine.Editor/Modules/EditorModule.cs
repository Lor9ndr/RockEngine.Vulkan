using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Core.Rendering.PipelineRenderers;
using RockEngine.Editor.Layers;
using RockEngine.Editor.SubPasses;

using SimpleInjector;

namespace RockEngine.Editor.Modules
{
    public class EditorModule : IDependencyModule
    {
        public void RegisterDependencies(Container container)
        {
            container.Collection.Append<ILayer, EditorLayer>();
            container.RegisterRenderSubPass<ImGuiPass, SwapchainPassStrategy>();

            //container.Collection.Append<ILayer, TitleBarLayer>();

        }
    }
}
