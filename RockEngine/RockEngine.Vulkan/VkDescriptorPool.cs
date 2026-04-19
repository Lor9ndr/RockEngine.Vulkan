using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public class VkDescriptorPool : VkObject<DescriptorPool>
    {
        private readonly VulkanContext _context;

        private VkDescriptorPool(VulkanContext context, in DescriptorPool descriptorPool)
            : base(descriptorPool)
        {
            _context = context;
        }

        public static VkDescriptorPool Create(VulkanContext context, in DescriptorPoolCreateInfo createInfo)
        {
            VulkanContext.Vk.CreateDescriptorPool(context.Device, in createInfo,
                in VulkanContext.CustomAllocator<VkDescriptorPool>(), out var descriptorPool)
                .VkAssertResult("Failed to create descriptor pool");

            return new VkDescriptorPool(context, descriptorPool);
        }

        /// <summary>
        /// Allocates a descriptor set using the provided allocate info.
        /// </summary>
        public Result AllocateDescriptorSet(VkDescriptorSetLayout setLayout, in DescriptorSetAllocateInfo allocInfo, out VkDescriptorSet set)
        {
            var result = VulkanContext.Vk.AllocateDescriptorSets(_context.Device, in allocInfo, out var descriptorSet);
            set = new VkDescriptorSet(_context, this, descriptorSet, setLayout);
            return result;
        }


        /// <summary>
        /// Frees a single descriptor set. (No caching – immediate free.)
        /// </summary>
        public unsafe void FreeDescriptorSet(VkDescriptorSet set)
        {
            var descriptorSet = set.VkObjectNative;
            VulkanContext.Vk.FreeDescriptorSets(_context.Device, this, 1, &descriptorSet);
        }

        public override void LabelObject(string name) =>
            _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.DescriptorPool, name);

        protected override unsafe void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_vkObject.Handle != default)
                {
                    VulkanContext.Vk.DestroyDescriptorPool(_context.Device, _vkObject,
                        in VulkanContext.CustomAllocator<VkDescriptorPool>());
                }
                _disposed = true;
            }
        }
    }
}