using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public class VkDescriptorPool : VkObject<DescriptorPool>
    {
        private readonly VulkanContext _context;
        private readonly Dictionary<VkDescriptorSetLayout, List<VkDescriptorSet>> _freeSets;
        private readonly Dictionary<VkDescriptorSet, VkDescriptorSetLayout> _setLayouts;

        private VkDescriptorPool(VulkanContext context, in DescriptorPool descriptorPool, bool reuseDescriptorSets)
            : base(in descriptorPool)
        {
            _context = context;
            if (reuseDescriptorSets)
            {
                _freeSets = new Dictionary<VkDescriptorSetLayout, List<VkDescriptorSet>>();
                _setLayouts = new Dictionary<VkDescriptorSet, VkDescriptorSetLayout>();
            }
        }

        public static VkDescriptorPool Create(VulkanContext context, in DescriptorPoolCreateInfo createInfo, bool reuseDescriptorSets = true)
        {
            VulkanContext.Vk.CreateDescriptorPool(context.Device, in createInfo, in VulkanContext.CustomAllocator<VkDescriptorPool>(), out var descriptorPool)
                .VkAssertResult("Failed to create descriptor pool");

            return new VkDescriptorPool(context, descriptorPool, reuseDescriptorSets);
        }

        public unsafe VkDescriptorSet AllocateDescriptorSet(VkDescriptorSetLayout setLayout)
        {
            if (_freeSets is not null && _freeSets.TryGetValue(setLayout, out var freeList) && freeList.Count > 0)
            {
                var set = freeList[^1];
                freeList.RemoveAt(freeList.Count - 1);
                return set;
            }

            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = this,
                DescriptorSetCount = 1,
                PSetLayouts = &setLayout.DescriptorSetLayout
            };
            VulkanContext.Vk.AllocateDescriptorSets(_context.Device, in allocInfo, out var descriptorSet)
                .VkAssertResult("Failed to allocate descriptor set");

            return new VkDescriptorSet(_context, this, in descriptorSet, setLayout);
        }

        public unsafe Result AllocateDescriptorSet(VkDescriptorSetLayout setLayout, out VkDescriptorSet set)
        {
            if (_freeSets is not null && _freeSets.TryGetValue(setLayout, out var freeList) && freeList.Count > 0)
            {
                set = freeList[^1];
                freeList.RemoveAt(freeList.Count - 1);
                return Result.Success;
            }

            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = this,
                DescriptorSetCount = 1,
                PSetLayouts = &setLayout.DescriptorSetLayout
            };
            var result = VulkanContext.Vk.AllocateDescriptorSets(_context.Device, in allocInfo, out var descriptorSet);
            set = new VkDescriptorSet(_context, this, in descriptorSet, setLayout);
            return result;
        }

        public override void LabelObject(string name) => _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.DescriptorPool, name);

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

        internal virtual void FreeDescriptorSet(VkDescriptorSet vkDescriptorSet)
        {
            if (_setLayouts  is not null &&  _setLayouts.TryGetValue(vkDescriptorSet, out var layout))
            {
                if (!_freeSets.ContainsKey(layout))
                    _freeSets[layout] = new List<VkDescriptorSet>();

                _freeSets[layout].Add(vkDescriptorSet);
            }
            else
            {
               _context.GraphicsSubmitContext.AddDependency(new TmpDisposable(vkDescriptorSet, _context));
            }
        }
        private record TmpDisposable(VkDescriptorSet DescriptorSet, VulkanContext Context) : IDisposable
        {

            public void Dispose()
            {
                VulkanContext.Vk.FreeDescriptorSets(Context.Device, DescriptorSet.Pool, [DescriptorSet.VkObjectNative]);
            }
        }
    }
}