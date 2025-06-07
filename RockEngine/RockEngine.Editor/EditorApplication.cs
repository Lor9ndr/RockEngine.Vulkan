using RockEngine.Core;

namespace RockEngine.Editor
{
    public class EditorApplication : Application
    {
        public EditorApplication(): base()
        {
        }

        protected override Task Load()
        {
            return Task.CompletedTask;
        }
    }
}
