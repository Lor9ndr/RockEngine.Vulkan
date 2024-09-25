
using Silk.NET.Vulkan;

using System.Drawing;
using System.Runtime.InteropServices;

using Buffer = Silk.NET.Vulkan.Buffer;

namespace RockEngine.Vulkan
{
    public record VkBuffer : VkObject<Buffer>
    {
        private readonly RenderingContext _context;
        private readonly VkDeviceMemory _deviceMemory;
        private readonly ulong _size;
        private readonly BufferUsageFlags _usage;

        public ulong Size => _size;

        public VkBuffer(RenderingContext context, in Buffer bufferNative, VkDeviceMemory deviceMemory, ulong size, BufferUsageFlags usage)
            : base(in bufferNative)
        {
            _context = context;
            _deviceMemory = deviceMemory;
            _size = size;
            _usage = usage;
            _size = usage switch
            {
                BufferUsageFlags.UniformBufferBit => GetAlignment(_size, _context.Device.PhysicalDevice.Properties.Limits.MinUniformBufferOffsetAlignment),
                _ => GetAlignment(_size, 256),
            };
        }

        public unsafe static VkBuffer Create(RenderingContext context, ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties)
        {
            var bufferInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = size,
                Usage = usage,
                SharingMode = SharingMode.Exclusive
            };
            
            RenderingContext.Vk.CreateBuffer(context.Device, in bufferInfo, null, out var bufferHandle)
                .VkAssertResult("Failed to create buffer");

            RenderingContext.Vk.GetBufferMemoryRequirements(context.Device, bufferHandle, out var memRequirements);

            var deviceMemory = VkDeviceMemory.Allocate(context, memRequirements, properties);

            RenderingContext.Vk.BindBufferMemory(context.Device, bufferHandle, deviceMemory, 0);

            return new VkBuffer(context, bufferHandle, deviceMemory, size, usage);
        }
        public unsafe static VkBuffer CreateAndCopyToStagingBuffer(RenderingContext context, void* data, ulong size )
        {
            var stagingBuffer = Create(context, size, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit);
            var destination = IntPtr.Zero.ToPointer();
            stagingBuffer.Map(ref destination);
            stagingBuffer.WriteToBuffer(data, destination);
            stagingBuffer.Flush();
            return stagingBuffer;
        }

        public unsafe void Flush(ulong size = Vk.WholeSize, ulong offset = 0)
        {
            var mappedRange = new MappedMemoryRange
            {
                SType = StructureType.MappedMemoryRange,
                Memory = _deviceMemory,
                Offset = offset,
                Size = size
            };
            RenderingContext.Vk.FlushMappedMemoryRanges(_context.Device, 1, &mappedRange)
                .VkAssertResult("Failed to flush mapped memory ranges");
        }

        public unsafe void CopyTo(VkBuffer dstBuffer, VkCommandPool commandPool, ulong srcOffset = 0, ulong dstOffset = 0)
        {
            using var commandBuffer = VkHelper.BeginSingleTimeCommands(commandPool);

            var copyRegion = new BufferCopy
            {
                SrcOffset = srcOffset,
                DstOffset = dstOffset,
                Size = _size
            };

            RenderingContext.Vk.CmdCopyBuffer(commandBuffer, this, dstBuffer, 1, &copyRegion);

            VkHelper.EndSingleTimeCommands(commandBuffer);
        }


        public unsafe void WriteToBuffer(void* data, void* destination, ulong size = Vk.WholeSize, ulong offset = 0)
        {
            switch (size)
            {
                case Vk.WholeSize:
                    System.Buffer.MemoryCopy(data, destination, _size, _size);
                    break;
                default:
                    {
                        if (size > _size)
                        {
                            return;
                        }

                        var memoryOffset = (ulong*)((ulong)destination + offset);
                        System.Buffer.MemoryCopy(data, memoryOffset, size, size);
                        break;
                    }
            }
        }
        public unsafe void WriteToBuffer(nint data, nint destination, ulong size = Vk.WholeSize, ulong offset = 0)
        {
            switch (size)
            {
                case Vk.WholeSize:
                    System.Buffer.MemoryCopy(data.ToPointer(), destination.ToPointer(), _size, _size);
                    break;
                default:
                    {
                        if (size > _size)
                        {
                            return;
                        }

                        var memoryOffset = (ulong*)((ulong)destination + offset);
                        System.Buffer.MemoryCopy(data.ToPointer(), memoryOffset, size, size);
                        break;
                    }
            }
        }

        public ValueTask WriteToBufferAsync<T>(T[] data, ulong size = Vk.WholeSize, ulong offset = 0) where T : unmanaged
        {
            if (data == null || data.Length == 0)
            {
                throw new ArgumentException("Data array is null or empty", nameof(data));
            }
            ulong dataSize = (ulong)(data.Length * Marshal.SizeOf<T>());

            if (size == Vk.WholeSize)
            {
                size = dataSize;
            }
            else if (size > dataSize)
            {
                throw new ArgumentException("Specified size is larger than the data array size", nameof(size));
            }

            if (offset + size > _size)
            {
                throw new ArgumentException("Data exceeds buffer size", nameof(size));
            }

            unsafe
            {
                void* destination = null;
                Map(ref destination, size, offset);

                /*MemoryMarshal.CreateSpan(ref destination, size);
                MemoryMarshal.Cast<T, byte>(data);*/
                try
                {
                    fixed (T* dataPtr = data)
                    {
                        WriteToBuffer(dataPtr, destination, size, 0);
                    }
                    Flush(size, offset);
                }
                finally
                {
                    Unmap();
                }
            }
            return default;
        }

        public ValueTask WriteToBufferAsync<T>(T data, ulong size = Vk.WholeSize, ulong offset = 0) where T : unmanaged
        {
            ulong dataSize = (ulong)(Marshal.SizeOf<T>());

            if (size == Vk.WholeSize)
            {
                size = dataSize;
            }
            else if (size > dataSize)
            {
                throw new ArgumentException("Specified size is larger than the data array size", nameof(size));
            }

            if (offset + size > _size)
            {
                throw new ArgumentException("Data exceeds buffer size", nameof(size));
            }

            unsafe
            {
                void* destination = null;
                Map(ref destination, size, offset);

                WriteToBuffer(&data, destination, size, 0);
                Flush(size, offset);

                Unmap();
            }
            return default;
        }


        public static ulong GetAlignment(ulong bufferSize, ulong minOffsetAlignment)
        {
            return minOffsetAlignment > 0 ? ((bufferSize - 1) / minOffsetAlignment + 1) * minOffsetAlignment : bufferSize;
        }


        public unsafe void Map(ref void* pdata, ulong size = Vk.WholeSize, ulong offset = 0)
        {
            RenderingContext.Vk.MapMemory(_context.Device, _deviceMemory, offset, size, 0, ref pdata)
                .VkAssertResult("Failed to mapMemory");
        }

        public unsafe void Map(out nint pdata, ulong size = Vk.WholeSize, ulong offset = 0)
        {
            void* mappedData = nint.Zero.ToPointer();
            Map(ref mappedData, size, offset);
            pdata = new nint(mappedData);
        }

        public void Unmap()
        {
            RenderingContext.Vk.UnmapMemory(_context.Device, _deviceMemory);
        }
        public void BindVertexBuffer(VkCommandBuffer commandBuffer, ulong vertexOffset = 0)
        {
            RenderingContext.Vk.CmdBindVertexBuffers(commandBuffer, 0, 1, in _vkObject, ref vertexOffset);
        }

        public void BindIndexBuffer(VkCommandBuffer commandBuffer, ulong indexOffset, IndexType type)
        {
            RenderingContext.Vk.CmdBindIndexBuffer(commandBuffer, _vkObject, indexOffset, type);
        }

        protected unsafe override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                RenderingContext.Vk.DestroyBuffer(_context.Device, _vkObject, null);
                _deviceMemory.Dispose();

                _disposed = true;
            }
        }

       
    }
}
