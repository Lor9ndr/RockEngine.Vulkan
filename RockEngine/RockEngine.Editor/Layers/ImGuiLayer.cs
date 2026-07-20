using ImGuiNET;

using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Editor.EditorUI.ImGuiRendering;
using RockEngine.Editor.EditorUI.UndoRedo;
using RockEngine.Vulkan;


namespace RockEngine.Editor.Layers
{
    public class ImGuiLayer : ILayer
    {
        private readonly ImGuiController _controller;
        private readonly WorldRenderer _renderer;
        private ImguiRenderCommand _command;

        public ImGuiLayer(ImGuiController controller, WorldRenderer renderer)
        {
            _controller = controller;
            _renderer = renderer;
            _command = new ImguiRenderCommand(controller.Render);
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
            _renderer.AddCommand(_command);
        }

        public void OnUpdate()
        {

            _controller.Update(_renderer);
            if (ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.Z))
            {
                UndoRedoService.Instance.Undo();
            }
            if (ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.Y))
            {
                UndoRedoService.Instance.Redo();
            }


            //}, null);

        }
    }
}
