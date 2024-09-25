using System;
using Silk.NET.Vulkan;
using RockEngine.Vulkan;
using RockEngine.Core.Rendering;
using RockEngine.Core.Helpers;

namespace RockEngine.Core
{
    public class UniformBuffer : IDisposable, IHaveBinding
    {
        private readonly RenderingContext _context;
        private readonly VkBuffer _buffer;
        private readonly ulong _size;
        private readonly bool _isDynamic;
        private bool _disposed;

        public Dictionary<DescriptorSetLayout, DescriptorSet> Bindings { get; set; }
        public VkBuffer Buffer => _buffer;
        public bool IsDynamic => _isDynamic;
        public ulong Size => _size;

        public UniformBuffer(RenderingContext context, ulong size, bool isDynamic = false)
        {
            _context = context;
            _size = size;
            _isDynamic = isDynamic;

            var bufferUsage = BufferUsageFlags.UniformBufferBit;
            var memoryProperties = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;

            if (isDynamic)
            {
                bufferUsage |= BufferUsageFlags.TransferDstBit;
            }

            _buffer = VkBuffer.Create(context, size, bufferUsage, memoryProperties);
        }

        public ValueTask UpdateAsync<T>(T data) where T : unmanaged
        {
            return Buffer.WriteToBufferAsync(data);
        }
        public ValueTask UpdateAsync<T>(T[] data) where T : unmanaged
        {
            return Buffer.WriteToBufferAsync(data);
        }

        public unsafe void UpdateSet(DescriptorSet set, DescriptorSetLayout setLayout,uint binding, uint dstArrayElement = 0)
        {
            switch (Bindings)
            {
                case null:
                    Bindings = new Dictionary<DescriptorSetLayout, DescriptorSet>()
                    {
                        {setLayout, set }
                    };
                    break;
                default:
                    Bindings[setLayout] = !Bindings.ContainsKey(setLayout) ? set : throw new InvalidOperationException("Already got binding on that setLayout");
                    break;
            }
            var bufferInfo = new DescriptorBufferInfo
            {
                Buffer = _buffer,
                Offset = 0,
                Range = _size
            };

            WriteDescriptorSet writeDescriptorSet = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstBinding = binding,
                DescriptorType = _isDynamic ? DescriptorType.UniformBufferDynamic : DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                DstArrayElement = dstArrayElement,
                DstSet = set,
                PBufferInfo = &bufferInfo
            };

            RenderingContext.Vk.UpdateDescriptorSets(_context.Device, 1, in writeDescriptorSet, 0, default);
        }

        public unsafe void Use(VkCommandBuffer commandBuffer, VkPipeline pipeline, uint dynamicOffset = 0, PipelineBindPoint bindPoint = PipelineBindPoint.Graphics)
        {
            foreach (var item in pipeline.Layout.DescriptorSetLayouts)
            {
                if (Bindings.TryGetValue(item.DescriptorSetLayout, out var set))
                {
                    RenderingContext.Vk.CmdBindDescriptorSets(commandBuffer, bindPoint, pipeline.Layout, 0, 1, in set, 0, &dynamicOffset);
                }
            }
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
