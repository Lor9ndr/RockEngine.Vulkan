
using Silk.NET.Vulkan;

using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RockEngine.Vulkan
{
    public record VkSemaphore : VkObject<Semaphore>
    {
        private readonly RenderingContext _context;
        public VkSemaphore(RenderingContext context, Semaphore semaphore)
            : base(semaphore)
        {
            _context = context;
        }

        public static VkSemaphore Create(RenderingContext context)
        {
            SemaphoreCreateInfo semaphoreCreateInfo = new SemaphoreCreateInfo
            {
                SType = StructureType.SemaphoreCreateInfo
            };

            RenderingContext.Vk.CreateSemaphore(context.Device, in semaphoreCreateInfo, in RenderingContext.CustomAllocator<VkSemaphore>(), out Semaphore semaphore)
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
                    RenderingContext.Vk.DestroySemaphore(_context.Device, _vkObject, in RenderingContext.CustomAllocator<VkSemaphore>());
                }

                _disposed = true;
            }
        }
    }
}