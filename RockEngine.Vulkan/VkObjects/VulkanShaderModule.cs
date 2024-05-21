using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    internal class VulkanShaderModule : VkObject
    {
        private readonly Vk _api;
        private readonly ShaderModule _module;
        private readonly VulkanLogicalDevice _device;
        public ShaderModule Module => _module;

        public VulkanShaderModule(Vk api, ShaderModule module, VulkanLogicalDevice device)
        {
            _api = api;
            _module = module;
            _device = device;
        }


        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                if (_module.Handle != default)
                {
                    unsafe
                    {
                        _api.DestroyShaderModule(_device.Device, _module, null);
                    }
                }

                _disposed = true;
            }
        }
    }
}
