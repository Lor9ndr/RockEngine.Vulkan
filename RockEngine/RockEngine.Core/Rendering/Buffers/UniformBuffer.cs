using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Buffers
{
    public class UniformBuffer : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly string _name;
        private readonly VkBuffer _buffer;
        private readonly bool _isDynamic;
        private bool _disposed;

        public VkBuffer Buffer => _buffer;
        public bool IsDynamic => _isDynamic;
        public ulong Size => _buffer.Size;

        public string Name => _name;
        public uint BindingLocation { get; }
        public ulong DynamicOffset { get; set; }
        public Dictionary<VkPipelineLayout, VkDescriptorSet> DescriptorSets { get; } = new Dictionary<VkPipelineLayout, VkDescriptorSet>();

        public UniformBuffer(VulkanContext context, string name, uint bindingLocation, ulong size,  bool isDynamic = false)
        {
            _context = context;
            _name = name;
            BindingLocation = bindingLocation;
            _isDynamic = isDynamic;

            var bufferUsage = BufferUsageFlags.UniformBufferBit;
            var memoryProperties = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;

            bufferUsage |= BufferUsageFlags.TransferDstBit;

            _buffer = VkBuffer.Create(context, size, bufferUsage, memoryProperties);
            _buffer.LabelObject(name);
        }

        public UniformBuffer(string name, uint bindingLocation, ulong size, bool isDynamic = false)
            : this(VulkanContext.GetCurrent(), name, bindingLocation, size, isDynamic)
        {
        }

        public ValueTask UpdateAsync<T>(in T data, ulong size = Vk.WholeSize, ulong offset = 0) where T : unmanaged
        {
            return Buffer.WriteToBufferAsync(data, size, offset);
        }
        public ValueTask UpdateAsync<T>(T[] data, ulong size = Vk.WholeSize, ulong offset = 0) where T : unmanaged
        {
            return Buffer.WriteToBufferAsync(data, size, offset);
        }
        public nint GetMappedData()
        {
            return _buffer.MappedData;
        }

        public void FlushBuffer(ulong size = Vk.WholeSize, ulong offset = 0)
        {
            _buffer.Flush(size, offset);
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
