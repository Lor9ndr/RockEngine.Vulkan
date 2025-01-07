using RockEngine.Core;
using RockEngine.Editor.Layers;

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
            await PushLayer(new EditorLayer(_world, _renderingContext,_graphicsEngine, _renderer, _inputContext));
/*            await PushLayer(new ImGuiLayer(_renderingContext, _graphicsEngine, _graphicsEngine.RenderPassManager, _inputContext));*/
        }
    }
}
