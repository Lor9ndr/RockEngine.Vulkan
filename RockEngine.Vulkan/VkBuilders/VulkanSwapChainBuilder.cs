using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

using System.Runtime.InteropServices;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class VulkanSwapChainBuilder
    {
        private uint _width = 800;
        private uint _height = 600;

        public VulkanSwapChainBuilder WithSize(uint width, uint height)
        {
            _width = width;
            _height = height;
            return this;
        }

        public unsafe VulkanSwapchain Build(VulkanContext context)
        {
            // Assume SwapChainSupportDetails, ChooseSwapSurfaceFormat, ChooseSwapPresentMode, and ChooseSwapExtent are implemented
            var swapChainSupport = VkHelper.QuerySwapChainSupport(context.Device.PhysicalDevice.VulkanObject, context.Surface);
            var surfaceFormat = ChooseSwapSurfaceFormat(swapChainSupport.Formats);
            var presentMode = ChooseSwapPresentMode(swapChainSupport.PresentModes);
            var extent = ChooseSwapExtent(swapChainSupport.Capabilities, _width, _height);

            uint imageCount = swapChainSupport.Capabilities.MinImageCount + 1;
            if (swapChainSupport.Capabilities.MaxImageCount > 0 && imageCount > swapChainSupport.Capabilities.MaxImageCount)
            {
                imageCount = swapChainSupport.Capabilities.MaxImageCount;
            }

            var createInfo = new SwapchainCreateInfoKHR
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = context.Surface.Surface,
                MinImageCount = imageCount,
                ImageFormat = surfaceFormat.Format,
                ImageColorSpace = surfaceFormat.ColorSpace,
                ImageExtent = extent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ColorAttachmentBit,
                PreTransform = swapChainSupport.Capabilities.CurrentTransform,
                CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
                PresentMode = presentMode,
                Clipped = Vk.True,
                OldSwapchain = default
            };

            // Handle queue family indices
            if (context.Device.QueueFamilyIndices.GraphicsFamily != context.Device.QueueFamilyIndices.PresentFamily)
            {
                uint[] queueFamilyIndices = new uint[2] { context.Device.QueueFamilyIndices.GraphicsFamily.Value, context.Device.QueueFamilyIndices.PresentFamily.Value };
                fixed (uint* pQueueFamilyIndices = queueFamilyIndices)
                {
                    createInfo.ImageSharingMode = SharingMode.Concurrent;
                    createInfo.QueueFamilyIndexCount = 2;
                    createInfo.PQueueFamilyIndices = pQueueFamilyIndices;
                }
            }
            else
            {
                createInfo.ImageSharingMode = SharingMode.Exclusive;
            }

            var swapchainApi = new KhrSwapchain(context.Api.Context);
            var result = swapchainApi.CreateSwapchain(context.Device.Device, in createInfo, null, out var swapChain);

            if (result != Result.Success)
            {
                throw new Exception("Failed to create swap chain");
            }

            uint countImages = 0;
            swapchainApi.GetSwapchainImages(context.Device.Device, swapChain, &countImages, null);
            var images = new Image[countImages];
            swapchainApi.GetSwapchainImages(context.Device.Device, swapChain, &countImages, images);

            return new VulkanSwapchain(context, swapChain, swapchainApi, images, surfaceFormat.Format, extent);
        }

        private Extent2D ChooseSwapExtent(SurfaceCapabilitiesKHR capabilities, uint width, uint height)
        {
            if (capabilities.CurrentExtent.Width != uint.MaxValue)
            {
                return capabilities.CurrentExtent;
            }
            else
            {
                Extent2D actualExtent = new Extent2D
                {
                    Width = Math.Max(capabilities.MinImageExtent.Width, Math.Min(capabilities.MaxImageExtent.Width, width)),
                    Height = Math.Max(capabilities.MinImageExtent.Height, Math.Min(capabilities.MaxImageExtent.Height, height))
                };

                return actualExtent;
            }
        }

        private PresentModeKHR ChooseSwapPresentMode(PresentModeKHR[] availablePresentModes)
        {
            foreach (var availablePresentMode in availablePresentModes)
            {
                if (availablePresentMode == PresentModeKHR.MailboxKhr)
                {
                    return availablePresentMode;
                }
            }

            // FIFO is always available as per Vulkan spec
            return PresentModeKHR.FifoKhr;
        }

        private SurfaceFormatKHR ChooseSwapSurfaceFormat(SurfaceFormatKHR[] availableFormats)
        {
            foreach (var availableFormat in availableFormats)
            {
                if (availableFormat.Format == Format.B8G8R8A8Srgb && availableFormat.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                {
                    return availableFormat;
                }
            }

            // If the preferred format is not found, return the first format available
            return availableFormats[0];
        }
    }
}
