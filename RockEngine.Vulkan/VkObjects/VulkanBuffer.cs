using RockEngine.Vulkan.Helpers;

using Silk.NET.Vulkan;

using System.Runtime.InteropServices;

using Buffer = Silk.NET.Vulkan.Buffer;

namespace RockEngine.Vulkan.VkObjects
{
    internal class VulkanBuffer : VkObject
    {
        private readonly Vk _api;
        private readonly VulkanLogicalDevice _device;
        private Buffer _buffer;
        private readonly DeviceMemory _deviceMemory;

        public Buffer Buffer => _buffer;
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public VulkanBuffer(Vk api, VulkanLogicalDevice device, Buffer buffer, DeviceMemory deviceMemory)
        {
            _api = api;
            _device = device;
            _buffer = buffer;
            _deviceMemory = deviceMemory;
        }

        public void Bind(VulkanCommandBuffer commandBuffer)
        {
            ulong offset = 0;
            _api.CmdBindVertexBuffers(commandBuffer.CommandBuffer, 0, 1, in _buffer, in offset);
        }

        public async Task SendDataAsync<T>(T[] data, ulong offset = 0) where T : struct
        {
            ulong bufferSize = (ulong)(data.Length * Marshal.SizeOf<T>());

            IntPtr dataPtr = MapMemory(bufferSize, offset);

            byte[] byteArray = await StructArrayToByteArrayAsync(data);
            Marshal.Copy(byteArray, 0, dataPtr, byteArray.Length);

            _api.UnmapMemory(_device.Device, _deviceMemory);
        }

        private unsafe IntPtr MapMemory(ulong bufferSize, ulong offset)
        {
            IntPtr dataPtr;
            void* mappedMemory = null;
            _api.MapMemory(_device.Device, _deviceMemory, offset, bufferSize, 0, &mappedMemory)
                .ThrowCode("Failed to map memory");
            dataPtr = new IntPtr(mappedMemory);
            return dataPtr;
        }

        public static  Task<byte[]> StructArrayToByteArrayAsync<T>(T[] data) where T : struct
        {
            return Task.Run(async () =>
            {
                await _semaphore.WaitAsync();
                try
                {
                    int size = Marshal.SizeOf<T>();
                    byte[] byteArray = new byte[data.Length * size];
                    IntPtr ptr = Marshal.AllocHGlobal(size);

                    try
                    {
                        var tasks = new List<Task>();

                        for (int i = 0; i < data.Length; i++)
                        {
                            int index = i; // Capture the current value of i
                            tasks.Add(Task.Run(() =>
                            {
                                Marshal.StructureToPtr(data[index], ptr, true);
                                Marshal.Copy(ptr, byteArray, index * size, size);
                            }));
                        }

                        await Task.WhenAll(tasks);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }

                    return byteArray;
                }
                finally
                {
                    _semaphore.Release();
                }
            });
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
                if (_buffer.Handle != 0)
                {
                    unsafe
                    {
                        _api.FreeMemory(_device.Device, _deviceMemory, null);
                        _api.DestroyBuffer(_device.Device, _buffer, null);
                    }
                    _buffer = default;
                }

                _disposed = true;
            }
        }
    }
}