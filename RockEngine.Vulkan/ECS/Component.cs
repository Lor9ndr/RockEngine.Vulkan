using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.ECS
{
    public abstract class Component
    {
        public abstract Task OnInitializedAsync(VulkanContext context);
        public abstract Task UpdateAsync(double time, VulkanContext context, CommandBufferWrapper commandBuffer);
    }
}
