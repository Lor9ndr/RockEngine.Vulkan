using NLog;

using RockEngine.Core;
using RockEngine.Core.DI;
using RockEngine.Editor.EditorUI.Logging;

namespace RockEngine.Editor
{
    public class EditorApplication : Application
    {
        /// <inheritdoc/>
        protected override Type GetContextType() => typeof(EditorContext);

        public EditorApplication():base()
        {
            // Container is already initialized here (base ctor called first)
            // You can safely do additional setup like logging configuration
            ConfigureLogging();
        }

        private void ConfigureLogging()
        {
            var config = new NLog.Config.LoggingConfiguration();
            var consoleTarget = new EditorConsoleTarget(IoC.Container.GetInstance<EditorConsole>());
            consoleTarget.Layout = "${time}|${level:uppercase=true}|${logger}|${message}${onexception:${newline}${exception:format=tostring:maxInnerExceptionLevel=10}}";
            config.AddTarget("EditorConsole", consoleTarget);
            config.AddRuleForAllLevels(consoleTarget);
            LogManager.Configuration = config;
        }
    }
}
