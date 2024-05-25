using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    internal class VulkanPipelineLayout:VkObject
    {
        private PipelineLayout _layout;
        private readonly VulkanContext _context;

        public PipelineLayout Layout => _layout;

        public VulkanPipelineLayout(VulkanContext context, PipelineLayout layout)
        {
            _layout = layout;
            _context = context;
        }

        protected override void Dispose(bool disposing)
        {

            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects) if any.
                }

                // Free unmanaged resources (unmanaged objects) and override a finalizer below.
                // Set large fields to null.
                if (_layout.Handle != 0)
                {
                    unsafe
                    {
                        _context.Api.DestroyPipelineLayout(_context.Device.Device, _layout, null);
                    }
                    _layout = default;
                }

                _disposed = true;
            }
        }
    }
}
