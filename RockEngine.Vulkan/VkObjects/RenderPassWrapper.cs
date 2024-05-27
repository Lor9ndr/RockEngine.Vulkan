using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public class RenderPassWrapper : VkObject
    {
        private readonly VulkanContext _context;
        private readonly RenderPass _renderPass;

        public RenderPassWrapper(VulkanContext context, RenderPass renderPass)
        {
            _context = context;
            _renderPass = renderPass;

        }

        public RenderPass RenderPass => _renderPass;

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
                    _context.Api.DestroyRenderPass(_context.Device.Device, _renderPass, null);
                }

                _disposed = true;
            }
        }
    }
}
