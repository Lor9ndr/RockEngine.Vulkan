using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public record VkDescriptorPool : VkObject<DescriptorPool>
    {
        private readonly VulkanContext _context;

        private VkDescriptorPool(VulkanContext context, in DescriptorPool descriptorPool)
            : base(in descriptorPool)
        {
            _context = context;
        }

        public static unsafe VkDescriptorPool Create(VulkanContext context, in DescriptorPoolCreateInfo createInfo)
        {
            VulkanContext.Vk.CreateDescriptorPool(context.Device, in createInfo, in VulkanContext.CustomAllocator<VkDescriptorPool>(), out var descriptorPool)
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
            VulkanContext.Vk.AllocateDescriptorSets(_context.Device, in allocInfo, out var descriptorSet)
                .VkAssertResult("Failed to allocate descriptor set");
            return descriptorSet;
        }

        public unsafe DescriptorSet AllocateDescriptorSet(VkDescriptorSetLayout setLayout)
        {
            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = this,
                DescriptorSetCount = 1,
                PSetLayouts = &setLayout.DescriptorSetLayout
            };
            VulkanContext.Vk.AllocateDescriptorSets(_context.Device, in allocInfo, out var descriptorSet)
                .VkAssertResult("Failed to allocate descriptor set");
            return descriptorSet;
        }

        protected override unsafe void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                if (_vkObject.Handle != default)
                {
                    VulkanContext.Vk.DestroyDescriptorPool(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkDescriptorPool>());
                }

                _disposed = true;
            }
        }
    }
}