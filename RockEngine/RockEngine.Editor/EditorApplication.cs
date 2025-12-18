using NLog;
using NLog.Targets;

using RockEngine.Core;
using RockEngine.Core.DI;
using RockEngine.Editor.EditorUI.Logging;
using RockEngine.Editor.Layers;

namespace RockEngine.Editor
{
    public class EditorApplication : Application
    {
        private readonly RenderDocIntegration _renderDoc;
        public EditorApplication(): base()
        {
            var config = new NLog.Config.LoggingConfiguration();
            var consoleTarget = new EditorConsoleTarget(IoC.Container.GetInstance<EditorConsole>());
            consoleTarget.Layout = "${shortdate}|${level:uppercase=true}|${logger}|${message}${onexception:${newline}${exception:format=tostring:maxInnerExceptionLevel=10}}"; // Custom layout
            config.AddTarget("EditorConsole", consoleTarget);
            config.AddRuleForAllLevels(consoleTarget);
            LogManager.Configuration = config;

            //_renderDoc = IoC.Container.GetInstance<RenderDocIntegration>();
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
