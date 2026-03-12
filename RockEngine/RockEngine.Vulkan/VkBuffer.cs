using Silk.NET.Vulkan;

using System;
using System.Runtime.InteropServices;

using Buffer = Silk.NET.Vulkan.Buffer;

namespace RockEngine.Vulkan
{
    public class VkBuffer : VkObject<Buffer>
    {
        private readonly VulkanContext _context;
        private readonly VkDeviceMemory _deviceMemory;
        private readonly BufferUsageFlags _usage;

        public ulong Size => _deviceMemory.Size;
        public nint MappedData => _deviceMemory.MappedData ?? throw new InvalidOperationException("Buffer is not mapped");

        public VkBuffer(VulkanContext context, in Buffer bufferNative, VkDeviceMemory deviceMemory, BufferUsageFlags usage)
            : base(in bufferNative)
        {
            _context = context;
            _deviceMemory = deviceMemory;
            _usage = usage;
            // Persistently map if memory is host-visible
            if ((_deviceMemory.Properties & MemoryPropertyFlags.HostVisibleBit) != 0)
            {
                MapPrivate();
            }
        }

        public static VkBuffer Create(VulkanContext context, ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties)
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

            VulkanContext.Vk.CreateBuffer(context.Device, in bufferInfo, in VulkanContext.CustomAllocator<VkBuffer>(), out var bufferHandle)
                .VkAssertResult("Failed to create buffer");

            VulkanContext.Vk.GetBufferMemoryRequirements(context.Device, bufferHandle, out var memRequirements);

            var deviceMemory = VkDeviceMemory.Allocate(context, memRequirements, properties);

            VulkanContext.Vk.BindBufferMemory(context.Device, bufferHandle, deviceMemory, 0);
            VulkanAllocator.DeviceMemoryTracker.AssociateObject(
               deviceMemory,
               bufferHandle.Handle,
               "Buffer",
               memRequirements.Size,
               0,
               bufferHandle);

            return new VkBuffer(context, bufferHandle, deviceMemory, usage);
        }

        public static unsafe VkBuffer CreateAndCopyToStagingBuffer(VulkanContext context, void* data, ulong size)
        {
            var stagingBuffer = Create(context, size, BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            using var memory = stagingBuffer.MapMemory();
            var destSpan = memory.GetSpan();
            var sourceSpan = new ReadOnlySpan<byte>(data, (int)size);
            sourceSpan.CopyTo(destSpan);
            memory.Flush();

            return stagingBuffer;
        }

        public static async ValueTask<VkBuffer> CreateAndCopyToStagingBuffer<T>(VulkanContext context, T[] data, ulong size) where T : unmanaged
        {
            var stagingBuffer = Create(context, size, BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            using var memory = stagingBuffer.MapMemory();
            var destSpan = memory.GetSpan();
            var sourceSpan = MemoryMarshal.AsBytes(data.AsSpan());
            sourceSpan.CopyTo(destSpan);
            memory.Flush();

            return stagingBuffer;
        }

        public unsafe void Flush(ulong size = Vk.WholeSize, ulong offset = 0)
        {
            if ((_deviceMemory.Properties & MemoryPropertyFlags.HostCoherentBit) != 0)
            {
                return;
            }

            ulong nonCoherentAtomSize = _context.Device.PhysicalDevice.Properties.Limits.NonCoherentAtomSize;
            ulong bufferSize = _deviceMemory.Size;

            ulong actualSize = (size == Vk.WholeSize) ? (bufferSize - offset) : size;
            actualSize = Math.Min(actualSize, bufferSize - offset);

            ulong alignedSize = (actualSize + nonCoherentAtomSize - 1) & ~(nonCoherentAtomSize - 1);
            alignedSize = Math.Min(alignedSize, bufferSize - offset);

            var mappedRange = new MappedMemoryRange
            {
                SType = StructureType.MappedMemoryRange,
                Memory = _deviceMemory,
                Offset = offset,
                Size = alignedSize
            };

            VulkanContext.Vk.FlushMappedMemoryRanges(_context.Device, 1, &mappedRange)
                .VkAssertResult("Failed to flush mapped memory ranges");
        }

        public unsafe void CopyTo(VkBuffer dstBuffer, UploadBatch batch, ulong srcOffset = 0, ulong dstOffset = 0)
        {
            var copyRegion = new BufferCopy
            {
                SrcOffset = srcOffset,
                DstOffset = dstOffset,
                Size = Size,
            };
            VulkanContext.Vk.CmdCopyBuffer(batch.CommandBuffer, this, dstBuffer, 1, in copyRegion);
        }

        public ValueTask WriteToBufferAsync<T>(T[] data, ulong size = Vk.WholeSize, ulong offset = 0) where T : unmanaged
        {
            WriteToBufferPrivate(data.AsSpan(), size, offset);
            return default;
        }

        public ValueTask WriteToBufferAsync<T>(in T data, ulong size = Vk.WholeSize, ulong offset = 0) where T : unmanaged
        {
            Span<T> singleItem = stackalloc T[] { data };
            WriteToBufferPrivate(singleItem, size, offset);
            return default;
        }

        public unsafe ValueTask WriteToBufferAsync<T>(void* data, ulong dataSize, ulong size = Vk.WholeSize, ulong offset = 0) where T : unmanaged
        {
            var span = new ReadOnlySpan<byte>(data, (int)dataSize);
            WriteToBufferPrivate(span, size, offset);
            return default;
        }

        public unsafe void WriteToBuffer<T>(void* data, ulong dataSize, ulong size = Vk.WholeSize, ulong offset = 0) where T : unmanaged
        {
            var span = new ReadOnlySpan<byte>(data, (int)dataSize);
            WriteToBufferPrivate(span, size, offset);
        }

        public void WriteToBuffer<T>(Span<T> data, ulong size = Vk.WholeSize, ulong offset = 0) where T : unmanaged
        {
            WriteToBufferPrivate(data, size, offset);
        }

        private void WriteToBufferPrivate<T>(Span<T> data, ulong size = Vk.WholeSize, ulong offset = 0) where T : unmanaged
        {
            if (data.IsEmpty || data.Length == 0)
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

            if (_deviceMemory.IsMapped)
            {
                using var memory = MapMemory(actualSize, offset);
                var destSpan = memory.GetSpan<T>();
                data.CopyTo(destSpan);
                Flush(actualSize, offset);
            }
            else
            {
                using var memory = MapMemory(actualSize, offset);
                var destSpan = memory.GetSpan<T>();
                data.CopyTo(destSpan);
                Flush(actualSize, offset);
            }
        }

        private unsafe void WriteToBufferPrivate(ReadOnlySpan<byte> data, ulong size = Vk.WholeSize, ulong offset = 0)
        {
            if (data.IsEmpty)
            {
                throw new ArgumentException("Data span is empty", nameof(data));
            }

            ulong dataSize = (ulong)data.Length;
            ulong actualSize = size;
            if (size == Vk.WholeSize)
            {
                actualSize = dataSize;
            }
            else if (actualSize > dataSize)
            {
                throw new ArgumentException("Specified size is larger than the data span size", nameof(size));
            }

            if (offset + actualSize > Size)
            {
                throw new ArgumentException("Data exceeds buffer size", nameof(size));
            }

            if (_deviceMemory.IsMapped)
            {
                var mappedPtr = _deviceMemory.MappedData!.Value;
                var destSpan = new Span<byte>((byte*)mappedPtr + offset, (int)actualSize);
                data.Slice(0, (int)actualSize).CopyTo(destSpan);

                if ((_deviceMemory.Properties & MemoryPropertyFlags.HostCoherentBit) == 0)
                {
                    Flush(actualSize, offset);
                }
            }
            else
            {
                using var memory = MapMemory(actualSize, offset);
                var destSpan = memory.GetSpan();
                data[..(int)actualSize].CopyTo(destSpan);
                memory.Flush();
            }
        }

        public static ulong GetAlignment(ulong bufferSize, ulong minOffsetAlignment)
        {
            return (bufferSize + minOffsetAlignment - 1) / minOffsetAlignment * minOffsetAlignment;
        }

        private void MapPrivate(ulong size = Vk.WholeSize, ulong offset = 0)
        {
            if (_deviceMemory.IsMapped)
            {
                return;
            }
            _deviceMemory.Map(size, offset);
        }

        private void MapPrivate(out nint pdata, ulong size = Vk.WholeSize, ulong offset = 0)
        {
            MapPrivate(size, offset);
            pdata = new nint(_deviceMemory.MappedData!.Value);
        }

        private void UnmapPrivate()
        {
            _deviceMemory.Unmap();
        }

        public void BindVertexBuffer(UploadBatch batch, ulong vertexOffset = 0)
        {
            batch.BindVertexBuffer(this, vertexOffset);
        }

        public void BindIndexBuffer(UploadBatch batch, ulong indexOffset, IndexType type)
        {
            batch.BindIndexBuffer(this, indexOffset, type);
        }

        public override void LabelObject(string name) => _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.Buffer, name);

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }
                if (_deviceMemory.IsMapped)
                {
                    //_deviceMemory.Unmap();
                }
                VulkanContext.Vk.DestroyBuffer(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkBuffer>());
                _deviceMemory.Dispose();
                VulkanAllocator.DeviceMemoryTracker.DisassociateObject(_vkObject.Handle);

                _disposed = true;
            }
        }

        /// <summary>
        /// Maps the buffer memory and returns a disposable mapped memory object
        /// </summary>
        /// <param name="size">Size to map</param>
        /// <param name="offset">Offset to map from</param>
        /// <returns>Disposable mapped memory object</returns>
        public MappedMemory MapMemory(ulong size = Vk.WholeSize, ulong offset = 0)
        {
            if(size == Vk.WholeSize)
            {
                size = Size;
            }
            MapPrivate(out nint mappedPtr, size, offset);
            return new MappedMemory(this, mappedPtr, size, offset);
        }

        internal void Unmap()
        {
            UnmapPrivate();
        }
    }
}