using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    internal class VulkanPipeline : VkObject
    {
        private readonly VulkanContext _context;
        private readonly Pipeline _pipeline;
        public Pipeline Pipeline => _pipeline;

        public VulkanPipeline(VulkanContext context, Pipeline pipeline)
        {
            _context = context;
            _pipeline = pipeline;
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
                    _context.Api.DestroyPipeline(_context.Device.Device, _pipeline, null);
                }
                _disposed = true;
            }
        }
    }
}
