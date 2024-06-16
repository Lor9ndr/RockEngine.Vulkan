using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public class DescriptorPoolWrapper : VkObject<DescriptorPool>
    {
        private readonly VulkanContext _context;
        private readonly uint _maxSets;


        public uint MaxSets => _maxSets;

        // Constructor
        private DescriptorPoolWrapper(VulkanContext context, in DescriptorPool descriptorPool, uint maxSets)
            :base(in descriptorPool)
        {
            _context = context;
            _maxSets = maxSets;
        }

        public unsafe static DescriptorPoolWrapper Create(VulkanContext context, in DescriptorPoolCreateInfo createInfo, uint maxSets)
        {
            context.Api.CreateDescriptorPool(context.Device, in createInfo, null, out var descriptorPool)
                .ThrowCode("Failed to create descriptor pool");

            return new DescriptorPoolWrapper(context, descriptorPool, maxSets);
        }


        protected unsafe override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                if (_vkObject.Handle != default)
                {
                    _context.Api.DestroyDescriptorPool(_context.Device, _vkObject, null);
                }

                _disposed = true;
            }
        }
    }
}