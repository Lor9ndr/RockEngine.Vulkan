using RockEngine.Vulkan.Helpers;

using Silk.NET.Vulkan;

using System;
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

        public VulkanBuffer(Vk api, VulkanLogicalDevice device, Buffer buffer, DeviceMemory deviceMemory)
        {
            _api = api;
            _device = device;
            _buffer = buffer;
            _deviceMemory = deviceMemory;
        }

        public void Use(ulong memoryOffset = 0)
        {
            _api.BindBufferMemory(_device.Device, _buffer, _deviceMemory, memoryOffset);
        }

        public void SendData<T>(T[] data, ulong offset = 0) where T : struct
        {
            ulong bufferSize = (ulong)(data.Length * Marshal.SizeOf<T>());

            IntPtr dataPtr;
            unsafe
            {
                void* mappedMemory;
                _api.MapMemory(_device.Device, _deviceMemory, offset, bufferSize, 0, &mappedMemory)
                    .ThrowCode("Failed to map memory");

                dataPtr = new IntPtr(mappedMemory);

                byte[] byteArray = StructArrayToByteArray(data);
                Marshal.Copy(byteArray, 0, dataPtr, byteArray.Length);

                _api.UnmapMemory(_device.Device, _deviceMemory);
            }
        }

        private byte[] StructArrayToByteArray<T>(T[] data) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] byteArray = new byte[data.Length * size];
            IntPtr ptr = Marshal.AllocHGlobal(size);

            for (int i = 0; i < data.Length; i++)
            {
                Marshal.StructureToPtr(data[i], ptr, true);
                Marshal.Copy(ptr, byteArray, i * size, size);
            }

            Marshal.FreeHGlobal(ptr);
            return byteArray;
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
                        _api.DestroyBuffer(_device.Device, _buffer, null);
                    }
                    _buffer = default;
                }

                _disposed = true;
            }
        }
    }
}