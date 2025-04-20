
using Silk.NET.Vulkan;

using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RockEngine.Vulkan
{
    public class VkSemaphore : VkObject<Semaphore>
    {
        private readonly VulkanContext _context;
        public VkSemaphore(VulkanContext context, Semaphore semaphore)
            : base(semaphore)
        {
            _context = context;
        }

        public static VkSemaphore Create(VulkanContext context)
        {
            SemaphoreCreateInfo semaphoreCreateInfo = new SemaphoreCreateInfo
            {
                SType = StructureType.SemaphoreCreateInfo
            };

            VulkanContext.Vk.CreateSemaphore(context.Device, in semaphoreCreateInfo, in VulkanContext.CustomAllocator<VkSemaphore>(), out Semaphore semaphore)
                .VkAssertResult("Failed to create semaphore.");

            return new VkSemaphore(context, semaphore);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                unsafe
                {
                    VulkanContext.Vk.DestroySemaphore(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkSemaphore>());
                }

                _disposed = true;
            }
        }
    }
}