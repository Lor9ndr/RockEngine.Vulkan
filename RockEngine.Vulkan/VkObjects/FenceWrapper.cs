using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public class FenceWrapper : VkObject<Fence>
    {
        private readonly VulkanContext _context;

        public FenceWrapper(VulkanContext context, in Fence fence)
            :base(fence)
        {
            _context = context;
        }

        public unsafe static FenceWrapper Create(VulkanContext context, in FenceCreateInfo fenceCreateInfo)
        {
            context.Api.CreateFence(context.Device, in fenceCreateInfo, null, out Fence fence)
                .ThrowCode("Failed to create fence.");

            return new FenceWrapper(context, in fence);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                unsafe
                {
                    _context.Api.DestroyFence(_context.Device, _vkObject, null);
                }

                _disposed = true;
            }
        }
    }
}