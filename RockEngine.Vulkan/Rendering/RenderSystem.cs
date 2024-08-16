using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.Rendering
{
    public abstract class RenderSystem : IDisposable
    {
        protected readonly VulkanContext _context;

        protected RenderSystem(VulkanContext context)
        {
            _context = context;
        }

        public abstract Task RenderAsync(Project p, FrameInfo frameInfo);
        public abstract void Dispose();
        
    }
}