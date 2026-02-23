using RockEngine.Core.Extensions;
using RockEngine.Core.Helpers;
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
            : base(CalculateTotalSize(appSettings.MaxCamerasSupported), true)
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

        /// <summary>
        /// Updates the UBO data for multiple cameras using safe span operations.
        /// </summary>
        public ValueTask UpdateAsync(GlobalUboData[] data)
        {
            if (data.Length > _maxCameras)
            {
                throw new ArgumentException($"Exceeded maximum cameras: {_maxCameras}", nameof(data));
            }

            // Map the entire buffer memory and obtain a Span<byte> over it
            using var mappedMemory = Buffer.MapMemory();
            for (int i = 0; i < data.Length; i++)
            {
                mappedMemory.WriteStrided(i, _alignedElementSize, in data[i]);
            }

            // Flush only the portion we actually wrote
            Buffer.Flush(_alignedElementSize * (ulong)data.Length, 0);
            return ValueTask.CompletedTask;
        }

        public UniformBufferBinding GetBinding(uint cameraIndex)
        {
            return _bindings[cameraIndex];
        }

        public ulong GetDynamicOffset(uint cameraIndex)
        {
            return _alignedElementSize * cameraIndex;
        }

        [GLSLStruct]
        public struct GlobalUboData
        {
            public Matrix4x4 ViewProj;
            public Matrix4x4 View;
            public Matrix4x4 Proj;
            public Matrix4x4 InvView;
            public Matrix4x4 InvProj;
            public Matrix4x4 InvViewProj;
            public System.Numerics.Vector4 CamPos;
            public Vector2 ScreenSize;
            public float FarClip;
            private float _padding1;
        }
    }
}