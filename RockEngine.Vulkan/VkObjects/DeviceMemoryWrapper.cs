using Silk.NET.Vulkan;
using RockEngine.Vulkan.VulkanInitilizers;
using RockEngine.Vulkan.Helpers;

namespace RockEngine.Vulkan.VkObjects
{
    public class DeviceMemory : VkObject<Silk.NET.Vulkan.DeviceMemory>
    {
        private readonly VulkanContext _context;
        private readonly Silk.NET.Vulkan.DeviceMemory _memory;
        private readonly ulong _size;
        public ulong Size => _size;

        private DeviceMemory(VulkanContext context, Silk.NET.Vulkan.DeviceMemory memory, ulong size)
            :base(memory)
        {
            _context = context;
            _memory = memory;
            _size = size;
        }


        public static unsafe DeviceMemory Allocate(VulkanContext context, MemoryRequirements memRequirements, MemoryPropertyFlags properties)
        {
            var allocInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = memRequirements.Size,
                MemoryTypeIndex = FindMemoryType(context, memRequirements.MemoryTypeBits, properties)
            };

           context.Api.AllocateMemory(context.Device, in allocInfo, null, out var memory)
                .ThrowCode("Failed to allocate memory!");

            return new DeviceMemory(context, memory, memRequirements.Size);
        }


        private static uint FindMemoryType(VulkanContext context, uint typeFilter, MemoryPropertyFlags properties)
        {
            context.Api.GetPhysicalDeviceMemoryProperties(context.Device.PhysicalDevice, out PhysicalDeviceMemoryProperties pMemoryProperties);
            for (uint i = 0; i < pMemoryProperties.MemoryTypeCount; i++)
            {
                if ((typeFilter & (1 << (int)i)) != 0 && (pMemoryProperties.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
                {
                    return i;
                }
            }
            throw new InvalidOperationException("Failed to find suitable memory type.");
        }
        public unsafe void MapMemory(ulong bufferSize, ulong offset, out IntPtr pData)
        {
            void* mappedMemory = null;
            _context.Api.MapMemory(_context.Device, _vkObject, offset, bufferSize, 0, &mappedMemory)
                .ThrowCode("Failed to map memory");
            pData = new nint(mappedMemory);
        }

        public unsafe void MapMemory(out IntPtr pData)
        {
            void* mappedMemory = null;
            _context.Api.MapMemory(_context.Device, _vkObject, 0, _size, 0, &mappedMemory)
                .ThrowCode("Failed to map memory");
            pData = new nint(mappedMemory);
        }

        public void Unmap()
        {
            _context.Api.UnmapMemory(_context.Device, _vkObject);
        }

        protected override unsafe void Dispose(bool disposing)
        {
            _context.Api.FreeMemory(_context.Device, _memory, null);
            _disposed = true;
        }

    }
}