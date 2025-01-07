using Silk.NET.Vulkan;
using RockEngine.Vulkan;

namespace RockEngine.Core
{
    public class UniformBuffer : IDisposable
    {
        private readonly RenderingContext _context;
        private readonly string _name;
        private readonly VkBuffer _buffer;
        private readonly bool _isDynamic;
        private bool _disposed;

        public VkBuffer Buffer => _buffer;
        public bool IsDynamic => _isDynamic;
        public ulong Size => _buffer.Size;

        public string Name => _name;
        public uint BindingLocation { get; }
        public uint DynamicOffset { get; set; }

        public UniformBuffer(RenderingContext context, string name, uint bindingLocation, ulong size, bool isDynamic = false)
        {
            _context = context;
            _name = name;
            BindingLocation = bindingLocation;
            _isDynamic = isDynamic;

            var bufferUsage = BufferUsageFlags.UniformBufferBit;
            var memoryProperties = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;

            if (isDynamic)
            {
                bufferUsage |= BufferUsageFlags.TransferDstBit;
            }

            _buffer = VkBuffer.Create(context, size, bufferUsage, memoryProperties);
        }

        public UniformBuffer(string name, uint bindingLocation, ulong size, bool isDynamic = false) 
            :this(RenderingContext.GetCurrent(), name, bindingLocation, size, isDynamic)
        {
        }

        public ValueTask UpdateAsync<T>(T data) where T : unmanaged
        {
            return Buffer.WriteToBufferAsync(data);
        }
        public ValueTask UpdateAsync<T>(T[] data) where T : unmanaged
        {
            return Buffer.WriteToBufferAsync(data);
        }


        public unsafe void Dispose()
        {
            if (!_disposed)
            {
                Buffer.Dispose();
                _disposed = true;
            }
        }

      
    }
}
