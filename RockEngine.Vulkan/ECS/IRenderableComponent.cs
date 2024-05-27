using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.ECS
{
    public interface IRenderableComponent
    {
        public void Render(VulkanContext context, CommandBufferWrapper commandBuffer);
    }
}
