using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

using System;
using System.Runtime.InteropServices;

using Buffer = Silk.NET.Vulkan.Buffer;

namespace RockEngine.Vulkan.VkObjects
{
    public class BufferWrapper : VkObject<Buffer>
    {
        private readonly VulkanContext _context;
        private DeviceMemory _deviceMemory;

        public ulong Size => _deviceMemory.Size;

        public DeviceMemory DeviceMemory => _deviceMemory;

        public BufferWrapper(VulkanContext context, in Buffer bufferNative, DeviceMemory deviceMemory, ulong size)
            :base(in bufferNative)
        {
            _context = context;
            _deviceMemory = deviceMemory;
        }

        public unsafe void AllocateMemory(MemoryPropertyFlags memoryFlag)
        {
            if (_deviceMemory != default)
            {
                throw new Exception("Memory is already allocated for that buffer");
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

        public async Task SendDataAsync<T>(T[] data, ulong offset = 0) where T : struct
        {
            MapMemory(Size, offset, out var dataPtr);

            byte[] byteArray = await StructArrayToByteArrayAsync(data);
            Marshal.Copy(byteArray, 0, dataPtr, byteArray.Length);

            _context.Api.UnmapMemory(_context.Device, _deviceMemory);
        }

        /// <summary>
        /// Sends data, no need to pre map the buffer, it is already done here
        /// </summary>
        /// <typeparam name="T">type of data is sending, struct only</typeparam>
        /// <param name="data">value of the data</param>
        /// <param name="offset">offset from 0 to size of data</param>
        /// <returns></returns>
        public async Task SendDataAsync<T>(T data, ulong offset = 0) where T : struct
        {
            MapMemory(Size, offset, out var dataPtr);

            byte[] byteArray = await StructArrayToByteArrayAsync(data);
            Marshal.Copy(byteArray, 0, dataPtr, byteArray.Length);

            UnmapMemory();
        }

        public void UnmapMemory() => _deviceMemory.Unmap();

        public async Task CopyBufferAsync(VulkanContext context, BufferWrapper dstBuffer, ulong size)
        {
            context.QueueMutex.WaitOne();
            try
            {
                var commandPool = context.GetOrCreateCommandPool();
                var commandBufferAllocateInfoo = new CommandBufferAllocateInfo()
                {
                    SType = StructureType.CommandBufferAllocateInfo,
                    Level = CommandBufferLevel.Primary,
                    CommandPool = commandPool,
                    CommandBufferCount = 1
                };
                using CommandBufferWrapper commandBuffer = CommandBufferWrapper.Create(in commandBufferAllocateInfoo, commandPool);

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
                    unsafe
                    {
                        SubmitInfo submitInfo = new SubmitInfo()
                        {
                            SType = StructureType.SubmitInfo,
                            CommandBufferCount = 1,
                            PCommandBuffers = buffer
                        };
                        context.Api.QueueSubmit(context.Device.GraphicsQueue, 1, in submitInfo, default);
                    }
                    context.Api.QueueWaitIdle(context.Device.GraphicsQueue);
                }
               
            }
            finally
            {
                context.QueueMutex.ReleaseMutex();
            }
        }

        public void MapMemory(ulong bufferSize, ulong offset, out nint pData)
        {
            _deviceMemory.MapMemory(bufferSize, offset, out pData);
        }

        public void MapMemory(out nint pData)
        {
           _deviceMemory.MapMemory(out pData);
        }

        public static ValueTask<byte[]> StructArrayToByteArrayAsync<T>(T data) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] byteArray = new byte[size];
            nint ptr = Marshal.AllocHGlobal(size);

            try
            {
                Marshal.StructureToPtr(data, ptr, true);
                Marshal.Copy(ptr, byteArray, 0, size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            return new ValueTask<byte[]>(byteArray);
        }

        public static Task<byte[]> StructArrayToByteArrayAsync<T>(T[] data) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] byteArray = new byte[data.Length * size];
            nint ptr = Marshal.AllocHGlobal(size);

            try
            {
                for (int i = 0; i < data.Length; i++)
                {
                    nint localPtr = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.StructureToPtr(data[i], localPtr, true);
                        Marshal.Copy(localPtr, byteArray, i * size, size);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(localPtr);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            return Task.FromResult(byteArray);
        }

        public unsafe static BufferWrapper Create(VulkanContext context, in BufferCreateInfo ci, MemoryPropertyFlags flags)
        {
            context.Api.CreateBuffer(context.Device, in ci, null, out Buffer buffer)
                .ThrowCode("Failed to create buffer");
            var memoryRequirements = context.Api.GetBufferMemoryRequirements(context.Device, buffer);

            var deviceMemory = DeviceMemory.Allocate(context, memoryRequirements, flags);

            context.Api.BindBufferMemory(context.Device, buffer, deviceMemory, 0);

            return new BufferWrapper(context, buffer, deviceMemory, memoryRequirements.Size);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects) if any.
                }

                // Free unmanaged resources (unmanaged objects) and override a finalizer below.
                // Set large fields to null.
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