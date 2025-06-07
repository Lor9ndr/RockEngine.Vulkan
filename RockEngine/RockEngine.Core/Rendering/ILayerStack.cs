using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering
{
    public interface ILayerStack
    {
        int Count { get; }

        void PopLayer(ILayer layer);
        Task PushLayer(ILayer layer);
        void Render(VkCommandBuffer vkCommandBuffer);
        void RenderImGui(VkCommandBuffer commandBuffer);
        void Update();
    }
}