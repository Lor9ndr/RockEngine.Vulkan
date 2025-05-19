using RockEngine.Core;
using RockEngine.Editor.Layers;

namespace RockEngine.Editor
{
    public class EditorApplication : Application
    {
        public EditorApplication(int width, int height)
            : base("RockEngine", width, height)
        {
        }

        protected override Task Load()
        {
           // await PushLayer(new TitleBarLayer(_window, _inputContext));
            return PushLayer(new EditorLayer(_world, _context, _graphicsEngine, _renderer, _inputContext, _textureStreamer));
        }
    }
}
