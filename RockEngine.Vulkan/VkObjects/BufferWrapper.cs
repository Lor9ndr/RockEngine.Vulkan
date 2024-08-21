using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Buffer = Silk.NET.Vulkan.Buffer;

namespace RockEngine.Vulkan.VkObjects
{
    public class BufferWrapper : VkObject<Buffer>
    {
        private readonly VulkanContext _context;
        private DeviceMemory _deviceMemory;

        public ulong Size => _deviceMemory.Size;
     
        public BufferWrapper(VulkanContext context, in Buffer bufferNative, DeviceMemory deviceMemory)
            : base(in bufferNative)
        {
            _context = context;
            _deviceMemory = deviceMemory;
        }

        public unsafe void AllocateMemory(MemoryPropertyFlags memoryFlag)
        {
            if (_deviceMemory != default)
            {
                throw new InvalidOperationException("Memory is already allocated for that buffer");
            }
            var memoryRequirements = _context.Api.GetBufferMemoryRequirements(_context.Device, _vkObject);
            _deviceMemory = DeviceMemory.Allocate(_context, memoryRequirements, memoryFlag);
        }

        public void BindVertexBuffer(CommandBufferWrapper commandBuffer, ulong offset = 0)
        {
            _context.Api.CmdBindVertexBuffers(commandBuffer, 0, 1, in _vkObject, in offset);
        }

        public void BindIndexBuffer(CommandBufferWrapper commandBuffer)
        {
            _context.Api.CmdBindIndexBuffer(commandBuffer, _vkObject, 0, IndexType.Uint32);
        }

        public unsafe ValueTask SendDataAsync<T>(ReadOnlyMemory<T> data, ulong offset = 0) where T : unmanaged
        {
            MapMemory(Size, offset);
            MemoryMarshal.AsBytes(data.Span).CopyTo(new Span<byte>(_deviceMemory.MappedData!.Value.ToPointer(), data.Length * Unsafe.SizeOf<T>()));
            UnmapMemory();
            return ValueTask.CompletedTask;
        }

        public unsafe ValueTask SendDataAsync<T>(T data, ulong offset = 0) where T : unmanaged
        {
            MapMemory(Size, offset);
            MemoryMarshal.Write(new Span<byte>(_deviceMemory.MappedData!.Value.ToPointer(), Unsafe.SizeOf<T>()), in data);
            UnmapMemory();
            return ValueTask.CompletedTask;
        }
        public unsafe ValueTask SendDataAsync<T>(T data) where T : unmanaged
        {
            MapMemory();
            MemoryMarshal.Write(new Span<byte>(_deviceMemory.MappedData!.Value.ToPointer(), (int)Size), in data);
            UnmapMemory();
            return ValueTask.CompletedTask;
        }
        public unsafe ValueTask SendDataAsync<T>(ReadOnlyMemory<T> data) where T : unmanaged
        {
            MapMemory();

            MemoryMarshal.AsBytes(data.Span).CopyTo(new Span<byte>(_deviceMemory.MappedData!.Value.ToPointer(), data.Length * Unsafe.SizeOf<T>()));
            UnmapMemory();
            return ValueTask.CompletedTask;
        }

        public unsafe ValueTask SendDataMappedAsync<T>(T data) where T : unmanaged
        {
            if (!_deviceMemory.IsMapped)
            {
                throw new InvalidOperationException("Failed to send data to unmapped buffer");
            }
            MemoryMarshal.Write(new Span<byte>((void*)_deviceMemory.MappedData!.Value, Unsafe.SizeOf<T>()), in data);
            return ValueTask.CompletedTask;
        }

        public unsafe ValueTask SendDataMappedAsync(Dictionary<string, object> parameters)
        {
            if (!_deviceMemory.IsMapped)
            {
                throw new InvalidOperationException("Failed to send data to unmapped buffer");
            }
            // Calculate the total size needed for the buffer
            int totalSize = 0;
            foreach (var param in parameters)
            {
                totalSize += GetSizeOfType(param.Value.GetType());
            }

            byte[] byteArray = new byte[totalSize];
            int offset = 0;

            foreach (var param in parameters)
            {
                byte[] paramBytes = SerializeParameter(param.Value);
                System.Buffer.BlockCopy(paramBytes, 0, byteArray, offset, paramBytes.Length);
                offset += paramBytes.Length;
            }

            fixed (byte* pData = byteArray)
            {
                System.Buffer.MemoryCopy(pData, (void*)_deviceMemory.MappedData.Value, totalSize, totalSize);
            }
            return ValueTask.CompletedTask;
        }

        private int GetSizeOfType(Type type)
        {
            if (type == typeof(float) || type == typeof(int))
                return 4;
            if (type == typeof(Vector2))
                return 8;
            if (type == typeof(Vector3))
                return 12;
            if (type == typeof(Vector4) || type == typeof(Quaternion))
                return 16;
            if (type == typeof(Matrix4x4))
                return 64;

            throw new ArgumentException($"Unsupported type: {type}");
        }

        private byte[] SerializeParameter(object param)
        {
            switch (param)
            {
                case float f:
                    return BitConverter.GetBytes(f);
                case int i:
                    return BitConverter.GetBytes(i);
                case Vector2 v2:
                    return new byte[8] {
                BitConverter.GetBytes(v2.X)[0], BitConverter.GetBytes(v2.X)[1], BitConverter.GetBytes(v2.X)[2], BitConverter.GetBytes(v2.X)[3],
                BitConverter.GetBytes(v2.Y)[0], BitConverter.GetBytes(v2.Y)[1], BitConverter.GetBytes(v2.Y)[2], BitConverter.GetBytes(v2.Y)[3]
            };
                case Vector3 v3:
                    return new byte[12] {
                BitConverter.GetBytes(v3.X)[0], BitConverter.GetBytes(v3.X)[1], BitConverter.GetBytes(v3.X)[2], BitConverter.GetBytes(v3.X)[3],
                BitConverter.GetBytes(v3.Y)[0], BitConverter.GetBytes(v3.Y)[1], BitConverter.GetBytes(v3.Y)[2], BitConverter.GetBytes(v3.Y)[3],
                BitConverter.GetBytes(v3.Z)[0], BitConverter.GetBytes(v3.Z)[1], BitConverter.GetBytes(v3.Z)[2], BitConverter.GetBytes(v3.Z)[3]
            };
                case Vector4 v4:
                    return new byte[16] {
                BitConverter.GetBytes(v4.X)[0], BitConverter.GetBytes(v4.X)[1], BitConverter.GetBytes(v4.X)[2], BitConverter.GetBytes(v4.X)[3],
                BitConverter.GetBytes(v4.Y)[0], BitConverter.GetBytes(v4.Y)[1], BitConverter.GetBytes(v4.Y)[2], BitConverter.GetBytes(v4.Y)[3],
                BitConverter.GetBytes(v4.Z)[0], BitConverter.GetBytes(v4.Z)[1], BitConverter.GetBytes(v4.Z)[2], BitConverter.GetBytes(v4.Z)[3],
                BitConverter.GetBytes(v4.W)[0], BitConverter.GetBytes(v4.W)[1], BitConverter.GetBytes(v4.W)[2], BitConverter.GetBytes(v4.W)[3]
            };
                case Quaternion q:
                    return new byte[16] {
                BitConverter.GetBytes(q.X)[0], BitConverter.GetBytes(q.X)[1], BitConverter.GetBytes(q.X)[2], BitConverter.GetBytes(q.X)[3],
                BitConverter.GetBytes(q.Y)[0], BitConverter.GetBytes(q.Y)[1], BitConverter.GetBytes(q.Y)[2], BitConverter.GetBytes(q.Y)[3],
                BitConverter.GetBytes(q.Z)[0], BitConverter.GetBytes(q.Z)[1], BitConverter.GetBytes(q.Z)[2], BitConverter.GetBytes(q.Z)[3],
                BitConverter.GetBytes(q.W)[0], BitConverter.GetBytes(q.W)[1], BitConverter.GetBytes(q.W)[2], BitConverter.GetBytes(q.W)[3]
            };
                case Matrix4x4 m:
                    {
                        byte[] result = new byte[64];
                        System.Buffer.BlockCopy(BitConverter.GetBytes(m.M11), 0, result, 0, 4);
                        System.Buffer.BlockCopy(BitConverter.GetBytes(m.M12), 0, result, 4, 4);
                        System.Buffer.BlockCopy(BitConverter.GetBytes(m.M13), 0, result, 8, 4);
                        System.Buffer.BlockCopy(BitConverter.GetBytes(m.M14), 0, result, 12, 4);
                        System.Buffer.BlockCopy(BitConverter.GetBytes(m.M21), 0, result, 16, 4);
                        System.Buffer.BlockCopy(BitConverter.GetBytes(m.M22), 0, result, 20, 4);
                        System.Buffer.BlockCopy(BitConverter.GetBytes(m.M23), 0, result, 24, 4);
                        System.Buffer.BlockCopy(BitConverter.GetBytes(m.M24), 0, result, 28, 4);
                        System.Buffer.BlockCopy(BitConverter.GetBytes(m.M31), 0, result, 32, 4);
                        System.Buffer.BlockCopy(BitConverter.GetBytes(m.M32), 0, result, 36, 4);
                        System.Buffer.BlockCopy(BitConverter.GetBytes(m.M33), 0, result, 40, 4);
                        System.Buffer.BlockCopy(BitConverter.GetBytes(m.M34), 0, result, 44, 4);
                        System.Buffer.BlockCopy(BitConverter.GetBytes(m.M41), 0, result, 48, 4);
                        System.Buffer.BlockCopy(BitConverter.GetBytes(m.M42), 0, result, 52, 4);
                        System.Buffer.BlockCopy(BitConverter.GetBytes(m.M43), 0, result, 56, 4);
                        System.Buffer.BlockCopy(BitConverter.GetBytes(m.M44), 0, result, 60, 4);
                        return result;
                    }

                default:
                    throw new ArgumentException($"Unsupported parameter type: {param.GetType()}");
            }
        }

        public void CopyBuffer(VulkanContext context, BufferWrapper dstBuffer, ulong size)
        {
            context.QueueMutex.WaitOne();
            var commandPool = context.GetOrCreateCommandPool();
            var commandBufferAllocateInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                Level = CommandBufferLevel.Primary,
                CommandPool = commandPool,
                CommandBufferCount = 1
            };
            using var commandBuffer = CommandBufferWrapper.Create(in commandBufferAllocateInfo, commandPool);

            commandBuffer.Begin(new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit
            });

            commandBuffer.CopyBuffer(this, dstBuffer, size);
            commandBuffer.End();

            unsafe
            {
                var buffer = stackalloc CommandBuffer[] { commandBuffer.VkObjectNative };
                SubmitInfo submitInfo = new SubmitInfo
                {
                    SType = StructureType.SubmitInfo,
                    CommandBufferCount = 1,
                    PCommandBuffers = buffer
                };
                context.Api.QueueSubmit(context.Device.GraphicsQueue, 1, in submitInfo, default);
            }
            context.Api.QueueWaitIdle(context.Device.GraphicsQueue);
            context.QueueMutex.ReleaseMutex();
        }

        public void MapMemory(ulong bufferSize, ulong offset)
        {
            _deviceMemory.MapMemory(bufferSize, offset);
        }

        public void MapMemory() => MapMemory(out _);

        public void MapMemory(out nint pData)
        {
            _deviceMemory.MapMemory();
            pData = _deviceMemory.MappedData!.Value;
        }

        public void UnmapMemory()
        {
            _deviceMemory.Unmap();
        }

        public static unsafe BufferWrapper Create(VulkanContext context, in BufferCreateInfo ci, MemoryPropertyFlags flags)
        {
            context.Api.CreateBuffer(context.Device, in ci, null, out Buffer buffer)
                .ThrowCode("Failed to create buffer");
            var memoryRequirements = context.Api.GetBufferMemoryRequirements(context.Device, buffer);

            var deviceMemory = DeviceMemory.Allocate(context, memoryRequirements, flags);

            context.Api.BindBufferMemory(context.Device, buffer, deviceMemory, 0);

            return new BufferWrapper(context, buffer, deviceMemory);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_vkObject.Handle != 0)
                {
                    unsafe
                    {
                        _deviceMemory.Dispose();
                        _context.Api.DestroyBuffer(_context.Device, _vkObject, null);
                    }
                    _vkObject = default;
                }

                _disposed = true;
            }
        }
    }
}
