using RockEngine.Vulkan.Rendering.MaterialRendering;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public class UniformBufferObject : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly BufferWrapper _uniformBuffer;
        private readonly ulong _size;
        private readonly string _name;

        public BufferWrapper UniformBuffer => _uniformBuffer;
        public ulong Size => _size;
        public string Name => _name;

        public Dictionary<PipelineLayout, (uint setIndex, DescriptorSet set)> PerPipelineDescriptorSet = new Dictionary<PipelineLayout, (uint setIndex, DescriptorSet set)>();

        public UniformBufferObject(VulkanContext context, BufferWrapper uniformBuffer, ulong size, string name)
        {
            _context = context;
            _uniformBuffer = uniformBuffer;
            _size  = size;
            _name = name;
        }
      
        public static UniformBufferObject Create(VulkanContext context, ulong size, string name)
        {
            // Create the uniform buffer
            BufferCreateInfo ci = new BufferCreateInfo()
            {
                SType = StructureType.BufferCreateInfo,
                Size = size,
                Usage = BufferUsageFlags.UniformBufferBit,
            };
            var uniformBuffer = BufferWrapper.Create(context, in ci, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            var ubo = new UniformBufferObject(context, uniformBuffer, size, name);
            return ubo;
        }


        public unsafe void Dispose()
        {
            UniformBuffer.Dispose();
        }
    }
}
