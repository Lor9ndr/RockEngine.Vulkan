using RockEngine.Core;
using RockEngine.Editor.Layers;

namespace RockEngine.Editor
{
    internal class EditorApplication : Application
    {
        public EditorApplication(int width, int height)
            : base("RockEngine", width, height)
        {
            OnLoad += Load;
        }

        private void Load()
        {
            PushLayer(new EditorLayer());
            PushLayer(new ImGuiLayer(_renderingContext, _graphicsEngine, _graphicsEngine.RenderPassManager, _inputContext));
        }
    }
}
