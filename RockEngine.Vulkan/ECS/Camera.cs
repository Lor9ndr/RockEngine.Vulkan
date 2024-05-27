using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.ECS
{
    internal class Camera : Component
    {
        public override Task OnInitializedAsync(VulkanContext context)
        {
            throw new NotImplementedException();
        }

        public override Task UpdateAsync(double time, VulkanContext context, CommandBufferWrapper commandBuffer)
        {
            throw new NotImplementedException();
        }
    }
}
