using RockEngine.Core;
using RockEngine.Editor.Layers;

using Silk.NET.SDL;

namespace RockEngine.Editor
{
    public class EditorApplication : Application
    {
        public EditorApplication(int width, int height)
            : base("RockEngine", width, height)
        {
            OnLoad += Load;
        }

        private async Task Load()
        {
            _window.WindowBorder = Silk.NET.Windowing.WindowBorder.Hidden;
            await PushLayer(new TitleBarLayer(_window, _inputContext));
            await PushLayer(new EditorLayer(_world, _renderingContext, _graphicsEngine, _renderer, _inputContext, _textureStreamer));
        }
    }
}
