using Silk.NET.Vulkan;

using System.Runtime.InteropServices;
using System.Text;


namespace RockEngine.Vulkan
{
    public static class VkHelper
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

       

        public static void BeginSingleTimeCommand(this VkCommandBuffer cmd)
        {
            var beginInfo = new CommandBufferBeginInfo()
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit
            };
            cmd.Begin(in beginInfo);
        }

       

        public static Result VkAssertResult(this Result result)
        {
            return result switch
            {
                Result.Success => result,
                _ => throw new Exception(result.ToString()),
            };
        }
        public static Result VkAssertResult(this Result result, string message)
        {
            return result switch
            {
                Result.Success => result,
                _ => throw new Exception(message + Environment.NewLine + result),
            };
        }
        public static Result VkAssertResult(this Result result, Result additionalCheck, string message)
        {
            if (result == Result.Success || result == additionalCheck)
            {
                return result;
            }
            throw new Exception(message + Environment.NewLine + result);
        }
        public static Result VkAssertResult(this Result result, string message, params Result[] additionalChecks)
        {
            if (result == Result.Success || additionalChecks.Contains(result))
            {
                return result;
            }
            throw new Exception(message + Environment.NewLine + result);
        }

        public static bool HasStencilComponent(this Format format)
        {
            return format == Format.D32SfloatS8Uint || format == Format.D24UnormS8Uint;
        }

    }
}