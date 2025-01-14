
using Silk.NET.Vulkan;

using System.Runtime.InteropServices;

using Buffer = Silk.NET.Vulkan.Buffer;

namespace RockEngine.Vulkan
{
    public record VkBuffer : VkObject<Buffer>
    {
        private readonly RenderingContext _context;
        private readonly VkDeviceMemory _deviceMemory;
        private readonly BufferUsageFlags _usage;
      
        public ulong Size => _deviceMemory.Size;

        public VkBuffer(RenderingContext context, in Buffer bufferNative, VkDeviceMemory deviceMemory, ulong size, BufferUsageFlags usage)
            : base(in bufferNative)
        {
            _context = context;
            _deviceMemory = deviceMemory;
            _usage = usage;
           
        }

        public unsafe static VkBuffer Create(RenderingContext context, ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties)
        {
            var allignmentSize = usage switch
            {
                BufferUsageFlags.UniformBufferBit | BufferUsageFlags.BufferUsageUniformBufferBit => GetAlignment(size, context.Device.PhysicalDevice.Properties.Limits.MinUniformBufferOffsetAlignment),
                _ => GetAlignment(size, 256),
            };
            var bufferInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = allignmentSize,
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

        public unsafe static VkBuffer CreateAndCopyToStagingBuffer(RenderingContext context, void* data, ulong size)
        {
            // Create a staging buffer with TransferSrcBit and HostVisibleBit
            var stagingBuffer = Create(context, size, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            // Map the staging buffer memory
            var destination = IntPtr.Zero.ToPointer();
            stagingBuffer.Map(ref destination);

            // Write data to the staging buffer
            stagingBuffer.WriteToBuffer(data, destination);

            // Flush the staging buffer to ensure data visibility
            stagingBuffer.Flush();

            // Unmap the memory
            stagingBuffer.Unmap();

            return stagingBuffer;
        }


        public static async ValueTask<VkBuffer> CreateAndCopyToStagingBuffer<T>(RenderingContext context, T[] data, ulong size) where T:unmanaged
        {
            var stagingBuffer = Create(context, size, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            await stagingBuffer.WriteToBufferAsync(data);
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
            using var commandBuffer = commandPool.BeginSingleTimeCommands();

            var copyRegion = new BufferCopy
            {
                SrcOffset = srcOffset,
                DstOffset = dstOffset,
                Size = Size,
            };

            RenderingContext.Vk.CmdCopyBuffer(commandBuffer, this, dstBuffer, 1, in copyRegion);

            commandBuffer.End();
            var cmdNative = commandBuffer.VkObjectNative;
            var submitInfo = new SubmitInfo() 
            { 
                SType = StructureType.SubmitInfo, 
                PCommandBuffers = &cmdNative, 
                CommandBufferCount = 1, 
            };
            _context.Device.GraphicsQueue.Submit(in submitInfo);
            _context.Device.GraphicsQueue.WaitIdle();

        }
        public unsafe void AddBufferMemoryBarrier(
            VkCommandBuffer commandBuffer,
            AccessFlags srcAccessMask,
            AccessFlags dstAccessMask,
            PipelineStageFlags srcStageMask,
            PipelineStageFlags dstStageMask
        )
        {
            var bufferMemoryBarrier = new BufferMemoryBarrier
            {
                SType = StructureType.BufferMemoryBarrier,
                SrcAccessMask = srcAccessMask,
                DstAccessMask = dstAccessMask,
                SrcQueueFamilyIndex = 0,
                DstQueueFamilyIndex = 0,
                Buffer = _vkObject,
                Offset = 0,
                Size = Size,
            };

            RenderingContext.Vk.CmdPipelineBarrier(
                commandBuffer,
                srcStageMask,
                dstStageMask,
                0,
                0,
                null,
                1,
                &bufferMemoryBarrier,
                0,
                null
            );
        }



        public unsafe void WriteToBuffer(void* data, void* destination, ulong size = Vk.WholeSize, ulong offset = 0)
        {
            switch (size)
            {
                case Vk.WholeSize:
                    System.Buffer.MemoryCopy(data, destination, Size, Size);
                    break;
                default:
                    {
                        if (size > Size)
                        {
                            throw new Exception("Size more than buffer size");
                        }

                        var memoryOffset = (ulong*)((ulong)destination + offset);
                        System.Buffer.MemoryCopy(data, memoryOffset, size, size);
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
            ulong actualSize = size;
            if (size == Vk.WholeSize)
            {
                actualSize = dataSize;
            }
            else if (actualSize > dataSize)
            {
                throw new ArgumentException("Specified size is larger than the data array size", nameof(size));
            }

            if (offset + actualSize > Size)
            {
                throw new ArgumentException("Data exceeds buffer size", nameof(size));
            }

            unsafe
            {
                void* destination = null;
                Map(ref destination, size, offset);
                try
                {
                    fixed (T* dataPtr = data)
                    {
                        WriteToBuffer(dataPtr, destination, actualSize, 0);
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
            ulong actualSize = size;
            if (size == Vk.WholeSize)
            {
                actualSize = dataSize;
            }
            else if (actualSize > dataSize)
            {
                throw new ArgumentException("Specified size is larger than the data array size", nameof(size));
            }

            if (offset + actualSize > Size)
            {
                throw new ArgumentException("Data exceeds buffer size", nameof(size));
            }

            unsafe
            {
                void* destination = null;
                Map(ref destination, size, offset);
                try
                {
                    WriteToBuffer(&data, destination, actualSize, 0);
                    Flush(size, offset);
                }
                finally
                {
                    Unmap();
                }
            }
            return default;
        }


        public static ulong GetAlignment(ulong bufferSize, ulong minOffsetAlignment)
        {
            return (bufferSize + minOffsetAlignment - 1) / minOffsetAlignment * minOffsetAlignment;
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
