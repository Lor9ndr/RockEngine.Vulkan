using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Editor.EditorComponents;
using RockEngine.Editor.EditorUI.ImGuiRendering;
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
            container.Collection.Append<ILayer, ImGuiLayer>();
            container.Collection.Append<ILayer, ProjectSelectionLayer>();
            container.Collection.Append<ILayer, EditorLayer>();
            container.Collection.Append<ILayer, AssetBrowserLayer>();
            container.Collection.Append<ILayer, DebugCamLayer>();
            container.RegisterRenderSubPass<ImGuiPass, SwapchainPassStrategy>();
            container.Register<EditorConsole>();
            container.Register<ImGuiController>();

            container.Register<DebugCamera>();

            //container.Collection.Append<ILayer, TitleBarLayer>();

        }
    }
}
