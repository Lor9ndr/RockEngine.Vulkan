using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    internal class VulkanPipelineLayout:VkObject
    {
        private readonly Vk _api;
        private PipelineLayout _layout;
        private readonly VulkanLogicalDevice _device;

        public PipelineLayout Layout => _layout;

        public VulkanPipelineLayout(Vk api, PipelineLayout layout, VulkanLogicalDevice device)
        {
            _api = api;
            _layout = layout;
            _device = device;
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
                        _api.DestroyPipelineLayout(_device.Device, _layout, null);
                    }
                    _layout = default;
                }

                _disposed = true;
            }
        }
    }
}
