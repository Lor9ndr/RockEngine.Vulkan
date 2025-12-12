using NLog;

using RockEngine.Core;
using RockEngine.Core.DI;
using RockEngine.Core.Shaders;
using RockEngine.Editor.EditorUI.Logging;
using RockEngine.Editor.Layers;

namespace RockEngine.Editor
{
    public class EditorApplication : Application
    {
        public EditorApplication(): base()
        {
            var config = new NLog.Config.LoggingConfiguration();
            var consoleTarget = new EditorConsoleTarget(IoC.Container.GetInstance<EditorConsole>());
            config.AddTarget("EditorConsole", consoleTarget);
            config.AddRuleForAllLevels(consoleTarget);
            LogManager.Configuration = config;
        }
        protected override async Task Load()
        {
            //await base.Load();
            var projectLayer = IoC.Container.GetInstance<ProjectSelectionLayer>();
            var imGuiLayer = IoC.Container.GetInstance<ImGuiLayer>();
            await _layerStack.PushLayer(imGuiLayer).ConfigureAwait(false);
            await _layerStack.PushLayer(projectLayer).ConfigureAwait(false);
        }

       
    }
}
