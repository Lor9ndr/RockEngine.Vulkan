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

        protected override Task Render(double time)
        {
            return base.Render(time);
        }

        protected override Task Update(double deltaTime)
        {
            return base.Update(deltaTime);
        }
    }
}
