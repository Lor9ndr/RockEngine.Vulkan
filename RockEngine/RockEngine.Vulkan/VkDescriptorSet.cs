using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public class VkDescriptorSet : VkObject<DescriptorSet>
    {
        private readonly VulkanContext _context;
        public bool IsDirty { get;set; } = true;

        public VkDescriptorPool Pool { get; }

        public VkDescriptorSetLayout SetLayout { get; }

        public VkDescriptorSet(VulkanContext context, VkDescriptorPool pool,in DescriptorSet vkObject, VkDescriptorSetLayout setLayout)
            :base(vkObject)
        {
            _context = context;
            Pool = pool;
            SetLayout = setLayout;

        }

        public override void LabelObject(string name) => _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.DescriptorSet, name);

        protected override void Dispose(bool disposing)
        {
            Pool.FreeDescriptorSet(this);
        }
    }
}
