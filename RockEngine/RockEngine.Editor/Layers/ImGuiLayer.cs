using ImGuiNET;

using RockEngine.Core;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Editor.EditorUI.ImGuiRendering;
using RockEngine.Vulkan;


namespace RockEngine.Editor.Layers
{
    internal class ImGuiLayer : ILayer
    {
        private readonly ImGuiController _controller;
        private readonly WorldRenderer _renderer;
        private readonly Application _app;

        public ImGuiLayer(ImGuiController controller, WorldRenderer renderer, Application app)
        {
            _controller = controller;
            _renderer = renderer;
            _app = app;
        }

        public Task OnAttach()
        {
            return Task.CompletedTask;
        }

        public void OnDetach()
        {
        }

        public void OnImGuiRender(UploadBatch batch)
        {

            ImGui.DockSpaceOverViewport(0, ImGui.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode);

        }

        public void OnRender(UploadBatch batch)
        {
        }

        public void OnUpdate()
        {
            _controller.Update(_renderer);
            _renderer.AddCommand(new ImguiRenderCommand(_controller.Render));
        }
    }
}
