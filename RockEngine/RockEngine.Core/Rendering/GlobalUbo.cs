using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering
{
    public sealed class GlobalUbo : UniformBuffer
    {
        private readonly ulong _alignedElementSize;
        private readonly VulkanContext _context;
        private readonly uint _maxCameras;
        private readonly UniformBufferBinding[] _bindings;

        public GlobalUbo(VulkanContext context, AppSettings appSettings)
            : base("GlobalUbo", 0, CalculateTotalSize(appSettings.MaxCamerasSupported), true)
        {
            _context = context;
            _maxCameras = appSettings.MaxCamerasSupported;
            _alignedElementSize = CalculateAlignedElementSize();
           
            _bindings = new UniformBufferBinding[_maxCameras];
            for (int i = 0; i < _maxCameras; i++)
            {
                _bindings[i] = new UniformBufferBinding(this, 0, 0, GetDynamicOffset((uint)i), _alignedElementSize);
            }
        }

        private ulong CalculateAlignedElementSize()
        {
            ulong elementSize = (ulong)Marshal.SizeOf<GlobalUboData>();
            ulong minAlignment = _context.Device.PhysicalDevice.Properties.Limits.MinUniformBufferOffsetAlignment;
            return (elementSize + minAlignment - 1) & ~(minAlignment - 1);
        }
        private static ulong CalculateTotalSize(uint maxCameras)
        {
            var context = VulkanContext.GetCurrent();
            ulong elementSize = (ulong)Marshal.SizeOf<GlobalUboData>();
            ulong minAlignment = context.Device.PhysicalDevice.Properties.Limits.MinUniformBufferOffsetAlignment;
            ulong alignedElementSize = (elementSize + minAlignment - 1) & ~(minAlignment - 1);
            return alignedElementSize * maxCameras;
        }

        public ValueTask UpdateAsync(uint cameraIndex, in GlobalUboData data)
        {
            uint index = cameraIndex;
            ulong offset = _alignedElementSize * index;
            return base.UpdateAsync(in data, (ulong)Marshal.SizeOf<GlobalUboData>(), offset);
        }
        public  Task UpdateAsync(GlobalUboData[] data)
        {
            /*//
            for (int i = 0; i < data.Length; i++)
            {
                GlobalUboData item = data[i];
                await UpdateAsync((uint)i, item);
            }
            //await  base.UpdateAsync(data);*/

            if (data.Length > _maxCameras)
            {
                throw new ArgumentException($"Exceeded maximum cameras: {_maxCameras}", nameof(data));
            }

            nint mappedPtr = GetMappedData();
            unsafe
            {
                for (int i = 0; i < data.Length; i++)
                {
                    byte* dest = (byte*)mappedPtr + (long)((ulong)i * _alignedElementSize);
                    *(GlobalUboData*)dest = data[i];
                }
            }
            FlushBuffer(_alignedElementSize * (ulong)data.Length, 0);
            return Task.CompletedTask;

        }
        public UniformBufferBinding GetBinding(uint cameraIndex)
        {
            return _bindings[cameraIndex];
        }

        public ulong GetDynamicOffset(uint cameraIndex)
        {
            return _alignedElementSize * cameraIndex;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GlobalUboData
        {
            public Matrix4x4 ViewProjection;
            public Vector3 Position;
            private float _padding;
        }
    }
}
