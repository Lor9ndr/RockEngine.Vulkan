using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.ECS
{
    // Should send ubos, textures and so on
    // has to have pipeline that will be created on InitializedAsync method 
    // pipeline has not to be hardcoded, pass params to the constructor
    internal class Material : Component
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
