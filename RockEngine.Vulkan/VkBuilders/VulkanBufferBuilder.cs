using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects;

using Silk.NET.Vulkan;

using Buffer = Silk.NET.Vulkan.Buffer;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class VulkanBufferBuilder : DisposableBuilder
    {
        private readonly Vk _api;
        private readonly VulkanLogicalDevice _device;
        private SharingMode _mode;
        private ulong _size;
        private BufferUsageFlags _usage;
        private MemoryPropertyFlags _propertyFlags;

        public VulkanBufferBuilder(Vk api, VulkanLogicalDevice device)
        {
            _api = api;
            _device = device;
        }

        public VulkanBufferBuilder Configure(SharingMode mode, ulong size, BufferUsageFlags usage, MemoryPropertyFlags propertyFlags)
        {
            _mode = mode;
            _size = size;
            _usage = usage;
            _propertyFlags = propertyFlags;
            return this;
        }

        public VulkanBuffer Build()
        {
            BufferCreateInfo ci = new BufferCreateInfo()
            {
                SType = StructureType.BufferCreateInfo,
                SharingMode = _mode,
                Usage = _usage,
                Size = _size,
            };
            unsafe
            {
                _api.CreateBuffer(_device.Device, ref ci, null, out Buffer buffer)
                    .ThrowCode("Failed to create buffer");
                var memoryRequirements = _api.GetBufferMemoryRequirements(_device.Device, buffer);
                
                MemoryAllocateInfo allocInfo = new MemoryAllocateInfo()
                {
                     SType = StructureType.MemoryAllocateInfo,
                     AllocationSize = memoryRequirements.Size,
                     MemoryTypeIndex = FindMemoryType(memoryRequirements.MemoryTypeBits, _propertyFlags)
                };
                _api.AllocateMemory(_device.Device, ref allocInfo, null, out DeviceMemory deviceMemory)
                    .ThrowCode("Failed to allocate buffer memory");
                _api.BindBufferMemory(_device.Device, buffer, deviceMemory,0);
                var bufferWrapper = new VulkanBuffer(_api, _device, buffer, deviceMemory);
                return bufferWrapper;
            }
        }

        private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
        {
            _api.GetPhysicalDeviceMemoryProperties(_device.PhysicalDevice.VulkanObject, out PhysicalDeviceMemoryProperties pMemoryProperties);
            for (uint i = 0; i < pMemoryProperties.MemoryTypeCount; i++)
            {
                if ((typeFilter & (1 << (int)i)) != 0 && (pMemoryProperties.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
                {
                    return i;
                }
            }
            throw new InvalidOperationException("Failed to find suitable memory type.");
        }
    }
}