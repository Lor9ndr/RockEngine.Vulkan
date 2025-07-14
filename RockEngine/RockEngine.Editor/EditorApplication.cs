using NLog;

using RockEngine.Core;
using RockEngine.Core.DI;
using RockEngine.Editor.EditorUI.Logging;

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
    }
}
