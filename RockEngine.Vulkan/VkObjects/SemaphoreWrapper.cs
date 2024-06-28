using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RockEngine.Vulkan.VkObjects
{
    public class SemaphoreWrapper : VkObject<Semaphore>
    {
        private readonly VulkanContext _context;
        public SemaphoreWrapper(VulkanContext context, Semaphore semaphore)
            :base(semaphore)
        {
            _context = context;
        }

        public static SemaphoreWrapper Create(VulkanContext context)
        {
            Semaphore semaphore;
            SemaphoreCreateInfo semaphoreCreateInfo = new SemaphoreCreateInfo
            {
                SType = StructureType.SemaphoreCreateInfo
            };

            unsafe
            {
                context.Api.CreateSemaphore(context.Device, &semaphoreCreateInfo, null, &semaphore)
                    .ThrowCode("Failed to create semaphore.");
            }

            return new SemaphoreWrapper(context, semaphore);
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
                    _context.Api.DestroySemaphore(_context.Device, _vkObject, null);
                }

                _disposed = true;
            }
        }
    }
}