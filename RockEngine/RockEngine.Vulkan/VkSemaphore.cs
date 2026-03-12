
using Silk.NET.Vulkan;

using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RockEngine.Vulkan
{
    public class VkSemaphore(VulkanContext context, Semaphore semaphore) : VkObject<Semaphore>(semaphore)
    {
        private readonly VulkanContext _context = context;

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
                    Vk.DestroySemaphore(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkSemaphore>());
                }

                _disposed = true;
            }
        }
        public override void LabelObject(string name) => _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.Semaphore, name);

    }
}