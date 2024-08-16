using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.Cache
{
    public class DescriptorLayoutCache : MemoryCache<uint, DescriptorSetLayout> , IDisposable
    {
        private readonly VulkanContext _context;

        public DescriptorLayoutCache(VulkanContext context)
        {
            _context = context;
        }
     
        public unsafe void Dispose()
        {
            foreach (var layout in _cache.Values)
            {
                _context.Api.DestroyDescriptorSetLayout(_context.Device, layout, null);
            }
            _cache.Clear();
        }
    }
}
