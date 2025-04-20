using RockEngine.Core.Rendering.Commands;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Vulkan;

using System.Collections.Concurrent;

namespace RockEngine.Core.Rendering.Passes
{
    public class ImGuiPass : RenderPass
    {
        private readonly VkSwapchain _swapchain;
        private readonly ConcurrentQueue<IRenderCommand> _commands;

        public ImGuiPass(
            VulkanContext context,
            BindingManager bindingManager,
            VkSwapchain swapchain,
            ConcurrentQueue<IRenderCommand> commands)
            : base(context, bindingManager)
        {
            _swapchain = swapchain;
            _commands = commands;
        }

        public override async Task Execute(VkCommandBuffer cmd, params object[] args)
        {
            while (_commands.TryPeek(out var command) && command is ImguiRenderCommand imguiCmd)
            {
                imguiCmd.RenderCommand.Invoke(cmd, _swapchain.Extent);
                while (!_commands.TryDequeue(out _)) { }
            }
        }
    }
}
