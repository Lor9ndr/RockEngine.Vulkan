using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

using System.Runtime.InteropServices;
using System.Text;


namespace RockEngine.Vulkan.Helpers
{
    internal static class VkHelper
    {
        public unsafe static bool IsExtensionSupported(string extension)
        {
            var api = Vk.GetApi();
            uint extensionCount = 0;
            api.EnumerateInstanceExtensionProperties((byte*)null, &extensionCount, null);
            ExtensionProperties[] availableExtensions = new ExtensionProperties[extensionCount];
            fixed (ExtensionProperties* pAvailableExtensions = availableExtensions)
            {
                api.EnumerateInstanceExtensionProperties((byte*)null, &extensionCount, pAvailableExtensions);
            }

            for (int i = 0; i < availableExtensions.Length; i++)
            {
                var ext = availableExtensions[i];
                // Convert the byte pointer to a byte array
                byte[] nameBytes = new byte[Vk.MaxExtensionNameSize]; // VK_MAX_EXTENSION_NAME_SIZE is 256
                Marshal.Copy((nint)ext.ExtensionName, nameBytes, 0, nameBytes.Length);
                // Find the null terminator to avoid converting unnecessary bytes
                int nullIndex = Array.IndexOf(nameBytes, (byte)0);
                string extensionName = Encoding.UTF8.GetString(nameBytes, 0, nullIndex >= 0 ? nullIndex : nameBytes.Length);

                if (extensionName == extension)
                {
                    return true;
                }
            }

            return false;
        }

        public static unsafe SwapChainSupportDetails QuerySwapChainSupport(PhysicalDevice device, ISurfaceHandler surface)
        {
            SwapChainSupportDetails details = new SwapChainSupportDetails();
            surface.SurfaceApi.GetPhysicalDeviceSurfaceCapabilities(device, surface.Surface, out details.Capabilities);

            uint formatCount;
            surface.SurfaceApi.GetPhysicalDeviceSurfaceFormats(device, surface.Surface, &formatCount, null);
            if (formatCount != 0)
            {
                details.Formats = new SurfaceFormatKHR[formatCount];
                surface.SurfaceApi.GetPhysicalDeviceSurfaceFormats(device, surface.Surface, &formatCount, out details.Formats[0]);
            }

            uint presentModeCount;
            surface.SurfaceApi.GetPhysicalDeviceSurfacePresentModes(device, surface.Surface, &presentModeCount, null);
            if (presentModeCount != 0)
            {
                details.PresentModes = new PresentModeKHR[presentModeCount];
                surface.SurfaceApi.GetPhysicalDeviceSurfacePresentModes(device, surface.Surface, &presentModeCount, out details.PresentModes[0]);
            }

            return details;
        }

        public static CommandBufferWrapper BeginSingleTimeCommands(VulkanContext context, CommandPoolWrapper commandPool)
        {
            var allocInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                Level = CommandBufferLevel.Primary,
                CommandPool = commandPool,
                CommandBufferCount = 1
            };
            var commandBuffer = CommandBufferWrapper.Create(context, in allocInfo, commandPool);

            commandBuffer.Begin(new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit
            });

            return commandBuffer;
        }

        public unsafe static void EndSingleTimeCommands(VulkanContext context, CommandBufferWrapper commandBuffer, CommandPoolWrapper commandPool)
        {
            context.QueueSemaphore.Wait();
            var vk = context.Api;
            var device = context.Device;
            commandBuffer.End();


            var commandBufferNative = commandBuffer.VkObjectNative;
            var submitInfo = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &commandBufferNative
            };
            vk.QueueSubmit(context.Device.GraphicsQueue, 1, in submitInfo, default);
            vk.QueueWaitIdle(context.Device.GraphicsQueue);
            vk.FreeCommandBuffers(device, commandPool, 1, in commandBufferNative);
            context.QueueSemaphore.Release();
        }

    }
}