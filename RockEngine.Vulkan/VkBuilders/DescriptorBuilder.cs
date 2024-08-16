using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;

namespace RockEngine.Vulkan.VkBuilders
{
    public class DescriptorBuilder
    {
        private readonly VulkanContext _context;
        private readonly List<WriteDescriptorSet> _writes = new List<WriteDescriptorSet>();
        private readonly List<DescriptorSetLayoutBinding> _bindings = new List<DescriptorSetLayoutBinding>();

        private DescriptorBuilder(VulkanContext context)
        {
            _context = context;
        }

        public static DescriptorBuilder Begin(VulkanContext context)
        {
            return new DescriptorBuilder(context);
        }

        public unsafe DescriptorBuilder BindBuffer(uint binding, DescriptorBufferInfo* bufferInfo, DescriptorType type, ShaderStageFlags stageFlags)
        {
            _bindings.Add(new DescriptorSetLayoutBinding
            {
                Binding = binding,
                DescriptorType = type,
                DescriptorCount = 1,
                StageFlags = stageFlags
            });

            _writes.Add(new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstBinding = binding,
                DescriptorCount = 1,
                DescriptorType = type,
                PBufferInfo = bufferInfo
            });

            return this;
        }

        public unsafe DescriptorBuilder BindImage(uint binding, DescriptorImageInfo* imageInfo, DescriptorType type, ShaderStageFlags stageFlags)
        {
            _bindings.Add(new DescriptorSetLayoutBinding
            {
                Binding = binding,
                DescriptorType = type,
                DescriptorCount = 1,
                StageFlags = stageFlags
            });

            _writes.Add(new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstBinding = binding,
                DescriptorCount = 1,
                DescriptorType = type,
                PImageInfo = imageInfo
            });

            return this;
        }

        public unsafe bool Build(out DescriptorSet set, out DescriptorSetLayout layout)
        {
            // Create layout
            fixed (DescriptorSetLayoutBinding* pBindings = _bindings.ToArray())
            {
                var layoutInfo = new DescriptorSetLayoutCreateInfo
                {
                    SType = StructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = (uint)_bindings.Count,
                    PBindings = pBindings
                };

                _context.Api.CreateDescriptorSetLayout(_context.Device, in layoutInfo, null, out layout)
                    .ThrowCode("Failed to create descriptor set layout");
            }

            // Allocate descriptor set
            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _context.DescriptorPoolFactory.GetOrCreatePool(),
                DescriptorSetCount = 1,
                PSetLayouts = (DescriptorSetLayout*)Unsafe.AsPointer(ref layout)
            };

            _context.Api.AllocateDescriptorSets(_context.Device, in allocInfo, out set)
                .ThrowCode("Failed to allocate descriptor set");

            // Update descriptor set
            for (int i = 0; i < _writes.Count; i++)
            {
                WriteDescriptorSet write = _writes[i];
                write.DstSet = set;
                _writes[i] = write;
            }

            fixed (WriteDescriptorSet* pWrites = _writes.ToArray())
            {
                _context.Api.UpdateDescriptorSets(_context.Device, (uint)_writes.Count, pWrites, 0, null);
            }

            return true;
        }
    }

}
