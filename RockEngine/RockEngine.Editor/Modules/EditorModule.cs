using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Editor.EditorComponents;
using RockEngine.Editor.EditorUI.ImGuiRendering;
using RockEngine.Editor.EditorUI.Logging;
using RockEngine.Editor.Layers;
using RockEngine.Editor.Rendering.Passes;
using RockEngine.Editor.Rendering.Passes.SubPasses;
using RockEngine.Editor.Selection;
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
            container.RegisterRenderPassStrategy<PickingPassStrategy>();
            container.RegisterRenderSubPass<PickingSubPass, PickingPassStrategy>();
            container.Register<EditorConsole>();
            container.Register<ImGuiController>();

            container.Register<DebugCamera>();
            container.Register<InfinityGrid>();
            container.Register<TransformGizmo>();
            container.Register<ISelectionManager, EntitySelectionManager>();

            //container.Collection.Append<ILayer, TitleBarLayer>();

        }
    }
}
