using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Editor.EditorUI.Logging;
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
            container.Register<EditorConsole>();

            //container.Collection.Append<ILayer, TitleBarLayer>();

        }
    }
}
