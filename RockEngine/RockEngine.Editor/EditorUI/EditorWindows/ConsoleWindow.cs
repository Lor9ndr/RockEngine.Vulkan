using RockEngine.Editor.EditorUI.Logging;

namespace RockEngine.Editor.EditorUI.EditorWindows
{
    public class ConsoleWindow : EditorWindow
    {
        private readonly EditorConsole _editorConsole;

        public ConsoleWindow(EditorConsole editorConsole) : base("Console")
        {
            _editorConsole = editorConsole;
        }

        protected override void OnDraw()
        {
            _editorConsole.Draw();
        }
    }
  
}