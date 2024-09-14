using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public record VkDeviceMemory : VkObject<DeviceMemory>
    {
        private readonly RenderingContext _context;
        private readonly DeviceMemory _memory;
        private readonly ulong _size;
        public ulong Size => _size;
        public bool IsMapped => _mappedData.HasValue;

        public nint? MappedData => _mappedData;

        private nint? _mappedData;

        private VkDeviceMemory(RenderingContext context, DeviceMemory memory, ulong size)
            : base(memory)
        {
            _context = context;
            _memory = memory;
            _size = size;
        }


        public static unsafe VkDeviceMemory Allocate(RenderingContext context, MemoryRequirements memRequirements, MemoryPropertyFlags properties)
        {
            var allocInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = memRequirements.Size,
                MemoryTypeIndex = FindMemoryType(context, memRequirements.MemoryTypeBits, properties)
            };

            RenderingContext.Vk.AllocateMemory(context.Device, in allocInfo, in RenderingContext.CustomAllocator, out var memory)
                 .VkAssertResult("Failed to allocate memory!");

            return new VkDeviceMemory(context, memory, memRequirements.Size);
        }


        private static uint FindMemoryType(RenderingContext context, uint typeFilter, MemoryPropertyFlags properties)
        {
            RenderingContext.Vk.GetPhysicalDeviceMemoryProperties(context.Device.PhysicalDevice, out PhysicalDeviceMemoryProperties pMemoryProperties);
            for (uint i = 0; i < pMemoryProperties.MemoryTypeCount; i++)
            {
                if ((typeFilter & 1 << (int)i) != 0 && (pMemoryProperties.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
                {
                    return i;
                }
            }
            throw new InvalidOperationException("Failed to find suitable memory type.");
        }

        /// <summary>
        /// Maps memory and set <see cref="MappedData"/> to its mapped value
        /// </summary>
        /// <param name="bufferSize">size of the buffer to map</param>
        /// <param name="offset">offset to map</param>
        public unsafe void Map(ulong bufferSize, ulong offset)
        {
            void* mappedMemory = null;
            RenderingContext.Vk.MapMemory(_context.Device, _vkObject, offset, bufferSize, 0, &mappedMemory)
                .VkAssertResult("Failed to map memory");
            _mappedData = new nint(mappedMemory);
        }

        public unsafe void Map()
        {
            void* mappedMemory = null;
            RenderingContext.Vk.MapMemory(_context.Device, _vkObject, 0, _size, 0, &mappedMemory)
                .VkAssertResult("Failed to map memory");
            _mappedData = new nint(mappedMemory);

        }
      

        public void Unmap()
        {
            RenderingContext.Vk.UnmapMemory(_context.Device, _vkObject);
            _mappedData = null;

        }

        protected override unsafe void Dispose(bool disposing)
        {
            RenderingContext.Vk.FreeMemory(_context.Device, _memory, in RenderingContext.CustomAllocator);
            _mappedData = null;
            _disposed = true;
        }

    }
}