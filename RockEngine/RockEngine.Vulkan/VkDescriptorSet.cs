using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public class VkDescriptorSet : VkObject<DescriptorSet>
    {
        private readonly VulkanContext _context;
        private readonly VkDescriptorPool _pool;
        public bool IsDirty { get;set; } = true;

        public VkDescriptorSet(VulkanContext context, VkDescriptorPool pool,in DescriptorSet vkObject)
            :base(vkObject)
        {
            _context = context;
            _pool = pool;
        }

      

        public override void LabelObject(string name) => _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.DescriptorSet, name);

        protected override void Dispose(bool disposing)
        {
            VulkanContext.Vk.FreeDescriptorSets(_context.Device, _pool,  1, in _vkObject);
        }
    }
}
