using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects;

using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace RockEngine.Vulkan.VkBuilders
{
    internal partial class VulkanLogicalDeviceBuilder : DisposableBuilder
    {
        public struct SwapChainSupportDetails
        {
            public SurfaceCapabilitiesKHR Capabilities;
            public SurfaceFormatKHR[] Formats;
            public PresentModeKHR[] PresentModes;
        }

        private readonly Vk _api;
        private readonly VulkanPhysicalDevice _physicalDevice;
        private readonly VulkanSurface _surface;
        private string[]? _extensions;

        public VulkanLogicalDeviceBuilder(Vk api, VulkanPhysicalDevice physicalDevice, VulkanSurface surface)
        {
            _api = api;
            _physicalDevice = physicalDevice;
            _surface = surface;
        }

        public VulkanLogicalDeviceBuilder WithExtensions(params string[] extensions)
        {
            _extensions = extensions;
            return this;
        }

        public VulkanLogicalDeviceBuilder WithSwapChainExtension()
        {
            var swapChainExtension = KhrSwapchain.ExtensionName;
            _extensions = _extensions?.Append(swapChainExtension).ToArray() ?? new[] { swapChainExtension };
            return this;
        }

        private bool IsDeviceSuitable(PhysicalDevice device)
        {
            ArgumentNullException.ThrowIfNull(_extensions, "_extensions");

            var indices = FindQueueFamilies(_api, device, _surface);
            bool extensionsSupported = CheckDeviceExtensionsSupported(device);
            bool swapChainAdequate = false;
            if (extensionsSupported)
            {
                SwapChainSupportDetails swapChainSupport = VkHelper.QuerySwapChainSupport(device, _surface);
                swapChainAdequate = swapChainSupport.Formats.Any() && swapChainSupport.PresentModes.Any();
            }
            return indices.IsComplete() && extensionsSupported && swapChainAdequate;
        }

        private unsafe bool CheckDeviceExtensionsSupported(PhysicalDevice device)
        {
            uint countExtensions = 0;
            _api.EnumerateDeviceExtensionProperties(_physicalDevice.VulkanObject, (byte*)null, ref countExtensions, (ExtensionProperties*)null);
            
            Span<ExtensionProperties> availableExtensions = stackalloc ExtensionProperties[(int)countExtensions];
            _api.EnumerateDeviceExtensionProperties(_physicalDevice.VulkanObject, (byte*)null, &countExtensions, availableExtensions);

            HashSet<string> requiredExtensions = _extensions.ToHashSet();

            foreach (var extension in availableExtensions)
            {
                requiredExtensions.Remove(SilkMarshal.PtrToString((nint)extension.ExtensionName));
            }
            return !requiredExtensions.Any();

        }


        public unsafe VulkanLogicalDevice Build()
        {
            var indices = FindQueueFamilies(_api, _physicalDevice.VulkanObject, _surface);

            HashSet<uint> uniqueQueueFamilies = new HashSet<uint>{indices.GraphicsFamily.Value, indices.PresentFamily.Value};
            List<DeviceQueueCreateInfo> queueCreateInfos = new List<DeviceQueueCreateInfo>();

            // Queue creation info
            var queuePriority = 1.0f;
            foreach (uint indicesFamily in uniqueQueueFamilies)
            {
                var queueCreateInfo = new DeviceQueueCreateInfo
                {
                    SType = StructureType.DeviceQueueCreateInfo,
                    QueueFamilyIndex = indicesFamily,
                    QueueCount = 1,
                    PQueuePriorities = &queuePriority
                };
                queueCreateInfos.Add(queueCreateInfo);
            }
          

            // Device creation
            var deviceCreateInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = 1,
                PQueueCreateInfos = (DeviceQueueCreateInfo*)CreateMemoryHandle(queueCreateInfos.ToArray()).Pointer
            };

            if(_extensions != null && IsDeviceSuitable(_physicalDevice.VulkanObject))
            {
                deviceCreateInfo.PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(_extensions);
                deviceCreateInfo.EnabledExtensionCount = (uint)_extensions.Length;
            }

            if (_api.CreateDevice(_physicalDevice.VulkanObject, in deviceCreateInfo, null, out Device logicalDevice) != Result.Success)
            {
                throw new Exception("Failed to create logical device.");
            }

            // Retrieve queue handle
            _api.GetDeviceQueue(logicalDevice, indices.GraphicsFamily.Value, 0, out Queue graphicsQueue);
            _api.GetDeviceQueue(logicalDevice, indices.PresentFamily.Value, 0, out Queue presentQueue);

            if (deviceCreateInfo.EnabledExtensionCount != 0)
            {
                SilkMarshal.Free((nint)deviceCreateInfo.PpEnabledExtensionNames);
            }

            return new VulkanLogicalDevice(_api, logicalDevice, graphicsQueue, presentQueue, indices, _physicalDevice);
        }

        private unsafe QueueFamilyIndices FindQueueFamilies(Vk api, PhysicalDevice device, VulkanSurface surface)
        {
            QueueFamilyIndices indices = new QueueFamilyIndices();

            uint queueFamilyCount = 0;
            api.GetPhysicalDeviceQueueFamilyProperties(device, ref queueFamilyCount, null);

            QueueFamilyProperties[] queueFamilies = new QueueFamilyProperties[queueFamilyCount];
            api.GetPhysicalDeviceQueueFamilyProperties(device, ref queueFamilyCount, out queueFamilies[0]);

            for (uint i = 0; i < queueFamilies.Length; i++)
            {
                Bool32 presentSupport = false;
                surface.SurfaceApi.GetPhysicalDeviceSurfaceSupport(device, i, surface.Surface, &presentSupport);

                if (presentSupport)
                {
                    indices.PresentFamily = i;
                }

                if (queueFamilies[i].QueueFlags.HasFlag(QueueFlags.GraphicsBit))
                {
                    indices.GraphicsFamily = i;
                }

                if (queueFamilies[i].QueueFlags.HasFlag(QueueFlags.ComputeBit))
                {
                    indices.ComputeFamily = i;
                }

                if (queueFamilies[i].QueueFlags.HasFlag(QueueFlags.TransferBit) &&
                    !queueFamilies[i].QueueFlags.HasFlag(QueueFlags.GraphicsBit) &&
                    !queueFamilies[i].QueueFlags.HasFlag(QueueFlags.ComputeBit))
                {
                    indices.TransferFamily = i;
                }

                if (indices.IsComplete())
                {
                    break;
                }
            }

            return indices;
        }

    }
}