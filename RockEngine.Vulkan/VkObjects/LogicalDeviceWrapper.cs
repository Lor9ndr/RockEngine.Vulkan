using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkBuilders;

using Silk.NET.Core.Native;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.VkObjects
{
    public struct SwapChainSupportDetails
    {
        public SurfaceCapabilitiesKHR Capabilities;
        public SurfaceFormatKHR[] Formats;
        public PresentModeKHR[] PresentModes;
    }

    public unsafe class LogicalDeviceWrapper : VkObject<Device>
    {
        private readonly Vk _api;
        private readonly Queue _presentQueue;
        private readonly Queue _graphicsQueue;
        internal readonly QueueFamilyIndices QueueFamilyIndices;
        private readonly PhysicalDeviceWrapper _physicalDevice;

        public Queue PresentQueue => _presentQueue;

        public Queue GraphicsQueue => _graphicsQueue;

        public PhysicalDeviceWrapper PhysicalDevice => _physicalDevice;

        internal LogicalDeviceWrapper(Vk api, Device device, Queue graphicsQueue, Queue presentQueue, QueueFamilyIndices indices, PhysicalDeviceWrapper physicalDevice)
            :base(device)
        {
            _api = api;
            _graphicsQueue = graphicsQueue;
            _presentQueue = presentQueue;
            QueueFamilyIndices = indices;
            _physicalDevice = physicalDevice;
        }


        protected unsafe override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                if (_vkObject.Handle != IntPtr.Zero)
                {
                    _api.DestroyDevice(_vkObject, null);
                    _vkObject = default;
                }

                _disposed = true;
            }
        }

        public static LogicalDeviceWrapper Create(Vk api, PhysicalDeviceWrapper physicalDevice, ISurfaceHandler surface, params string[] extensions)
        {
            // Find queue families
            QueueFamilyIndices indices = FindQueueFamilies(api, physicalDevice, surface);

            // Check if the device is suitable
            if (!IsDeviceSuitable(api, physicalDevice, surface, extensions, indices))
            {
                throw new Exception("Device is not suitable.");
            }

            // Create queue create info
            HashSet<uint> uniqueQueueFamilies = new HashSet<uint> { indices.GraphicsFamily.Value, indices.PresentFamily.Value };
            DeviceQueueCreateInfo[] queueCreateInfos = new DeviceQueueCreateInfo[uniqueQueueFamilies.Count];
            float queuePriority = 1.0f;

            for (int i = 0; i < uniqueQueueFamilies.Count; i++)
            {
                var queueCreateInfo = new DeviceQueueCreateInfo
                {
                    SType = StructureType.DeviceQueueCreateInfo,
                    QueueFamilyIndex = uniqueQueueFamilies.ElementAt(i),
                    QueueCount = 1,
                    PQueuePriorities = &queuePriority
                };
                queueCreateInfos[i] = queueCreateInfo;
            }

            PhysicalDeviceFeatures deviceFeatures = new PhysicalDeviceFeatures()
            {
                 SamplerAnisotropy = true,
                 DepthClamp = true,
            };

            using var pqueueCreateInfo = queueCreateInfos.AsMemory().Pin();
            // Create device create info
            var deviceCreateInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = (uint)queueCreateInfos.Length,
                PQueueCreateInfos = (DeviceQueueCreateInfo*)pqueueCreateInfo.Pointer,
                PEnabledFeatures = &deviceFeatures
                
            };

            // Set extensions
            if (extensions != null && extensions.Length > 0)
            {
                deviceCreateInfo.PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(extensions);
                deviceCreateInfo.EnabledExtensionCount = (uint)extensions.Length;
            }

            // Create the logical device
            if (api.CreateDevice(physicalDevice, in deviceCreateInfo, null, out Device logicalDevice) != Result.Success)
            {
                throw new Exception("Failed to create logical device.");
            }

            // Retrieve queue handles
            api.GetDeviceQueue(logicalDevice, indices.GraphicsFamily.Value, 0, out Queue graphicsQueue);
            api.GetDeviceQueue(logicalDevice, indices.PresentFamily.Value, 0, out Queue presentQueue);

            // Free unmanaged memory
            if (deviceCreateInfo.EnabledExtensionCount != 0)
            {
                SilkMarshal.Free((nint)deviceCreateInfo.PpEnabledExtensionNames);
            }
            SilkMarshal.Free((nint)deviceCreateInfo.PQueueCreateInfos);

            return new LogicalDeviceWrapper(api, logicalDevice, graphicsQueue, presentQueue, indices, physicalDevice);
        }

        private static unsafe QueueFamilyIndices FindQueueFamilies(Vk api, PhysicalDevice device, ISurfaceHandler surface)
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

        private static bool IsDeviceSuitable(Vk api, PhysicalDevice device, ISurfaceHandler surface, string[] extensions, QueueFamilyIndices indices)
        {
            ArgumentNullException.ThrowIfNull(extensions);

            bool extensionsSupported = CheckDeviceExtensionsSupported(api, device, extensions);
            bool swapChainAdequate = false;
            if (extensionsSupported)
            {
                SwapChainSupportDetails swapChainSupport = VkHelper.QuerySwapChainSupport(device, surface);
                swapChainAdequate = swapChainSupport.Formats.Length != 0 && swapChainSupport.PresentModes.Length != 0;
            }
            var features = api.GetPhysicalDeviceFeatures(device);
            return indices.IsComplete() && extensionsSupported && swapChainAdequate && features.SamplerAnisotropy;
        }

        private static unsafe bool CheckDeviceExtensionsSupported(Vk api, PhysicalDevice device, string[] extensions)
        {
            uint countExtensions = 0;
            api.EnumerateDeviceExtensionProperties(device, (byte*)null, ref countExtensions, (ExtensionProperties*)null);

            Span<ExtensionProperties> availableExtensions = stackalloc ExtensionProperties[(int)countExtensions];
            api.EnumerateDeviceExtensionProperties(device, (byte*)null, &countExtensions, availableExtensions);

            HashSet<string> requiredExtensions = extensions.ToHashSet();

            foreach (var extension in availableExtensions)
            {
                requiredExtensions.Remove(SilkMarshal.PtrToString((nint)extension.ExtensionName));
            }
            return requiredExtensions.Count == 0;
        }
    }
}