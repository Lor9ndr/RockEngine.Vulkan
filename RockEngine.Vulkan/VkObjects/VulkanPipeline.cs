using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    internal class VulkanPipeline : VkObject
    {
        private readonly Vk _api;
        private readonly VulkanLogicalDevice _device;
        private readonly Pipeline _pipeline;
        public Pipeline Pipeline => _pipeline;

        public VulkanPipeline(Vk api, VulkanLogicalDevice device, Pipeline pipeline)
        {
            _api = api;
            _device = device;
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
                    _api.DestroyPipeline(_device.Device, _pipeline, null);
                }
                _disposed = true;
            }
        }
    }
}
