using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public class Image : VkObject<Silk.NET.Vulkan.Image>
    {
        private readonly DeviceMemory _imageMemory;
        private readonly VulkanContext _context;

        private Image(VulkanContext context, Silk.NET.Vulkan.Image vkImage, DeviceMemory imageMemory)
            : base(vkImage)

        {
            _imageMemory = imageMemory;
            _context = context;
        }

        public unsafe static Image Create(VulkanContext context, in ImageCreateInfo ci)
        {
            context.Api.CreateImage(context.Device, in ci, null, out var vkImage)
                .ThrowCode("Failed to create image!");
            context.Api.GetImageMemoryRequirements(context.Device, vkImage, out var memRequirements);

            var imageMemory = DeviceMemory.Allocate(context, memRequirements, MemoryPropertyFlags.DeviceLocalBit);

            context.Api.BindImageMemory(context.Device, vkImage, imageMemory, 0);
            return new Image(context, vkImage, imageMemory);
        }

        protected unsafe override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _context.Api.DestroyImage(_context.Device, _vkObject, null);
                _imageMemory.Dispose();
                _disposed = true;
            }
        }
    }
}
