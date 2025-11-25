using ImGuiNET;

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

        public ImGuiLayer(ImGuiController controller, WorldRenderer renderer)
        {
            _controller = controller;
            _renderer = renderer;
        }

        public Task OnAttach()
        {
            return Task.CompletedTask;
        }

        public void OnDetach()
        {
        }

        public ValueTask OnImGuiRender(VkCommandBuffer vkCommandBuffer)
        {
            ImGui.DockSpaceOverViewport(ImGui.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode);
            return ValueTask.CompletedTask;
        }

        public void OnRender(VkCommandBuffer vkCommandBuffer)
        {
        }

        public void OnUpdate()
        {
            _controller.Update();
            _renderer.AddCommand(new ImguiRenderCommand(_controller.Render));
        }
    }
}
