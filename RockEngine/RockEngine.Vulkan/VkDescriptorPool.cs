using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public record VkDescriptorPool : VkObject<DescriptorPool>
    {
        private readonly RenderingContext _context;

        private VkDescriptorPool(RenderingContext context, in DescriptorPool descriptorPool)
            : base(in descriptorPool)
        {
            _context = context;
        }

        public unsafe static VkDescriptorPool Create(RenderingContext context, in DescriptorPoolCreateInfo createInfo)
        {
            RenderingContext.Vk.CreateDescriptorPool(context.Device, in createInfo, in RenderingContext.CustomAllocator, out var descriptorPool)
                .VkAssertResult("Failed to create descriptor pool");

            return new VkDescriptorPool(context, descriptorPool);
        }


        public unsafe DescriptorSet AllocateDescriptorSet(DescriptorSetLayout setLayout)
        {
            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = this,
                DescriptorSetCount = 1,
                PSetLayouts = &setLayout
            };
            RenderingContext.Vk.AllocateDescriptorSets(_context.Device, in allocInfo, out var descriptorSet)
                .VkAssertResult("Failed to allocate descriptor set");
            return descriptorSet;
        }

        protected unsafe override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                if (_vkObject.Handle != default)
                {
                    RenderingContext.Vk.DestroyDescriptorPool(_context.Device, _vkObject, in RenderingContext.CustomAllocator);
                }

                _disposed = true;
            }
        }
    }
}