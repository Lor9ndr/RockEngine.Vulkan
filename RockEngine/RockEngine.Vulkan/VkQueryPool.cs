using Silk.NET.Vulkan;

using System;

namespace RockEngine.Vulkan
{
    public class VkQueryPool : VkObject<QueryPool>
    {
        private readonly VulkanContext _context;
        private bool _disposed;

        private VkQueryPool(QueryPool vkObject, VulkanContext context)
            : base(vkObject)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public override void LabelObject(string name)
        {
            _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.QueryPool, name);
        }

        public static unsafe VkQueryPool Create(VulkanContext context, in QueryPoolCreateInfo createInfo)
        {
            QueryPool queryPool;
            fixed (QueryPoolCreateInfo* pCreateInfo = &createInfo)
            {
                VulkanContext.Vk.CreateQueryPool(context.Device, pCreateInfo, in VulkanContext.CustomAllocator<VkQueryPool>(), &queryPool)
                    .VkAssertResult();
            }
            return new VkQueryPool(queryPool, context);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // No managed resources to dispose
                }

                // Destroy Vulkan query pool
                VulkanContext.Vk.DestroyQueryPool(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkQueryPool>());
                _disposed = true;
            }
        }

        // Optional: Add reset functionality if needed
        public void Reset(uint firstQuery = 0, uint queryCount = 1)
        {
            unsafe
            {
                VulkanContext.Vk.ResetQueryPool(_context.Device, _vkObject, firstQuery, queryCount);
            }
        }
    }
}