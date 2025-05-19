using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public struct SwapChainSupportDetails
    {
        public SurfaceCapabilitiesKHR Capabilities;
        public SurfaceFormatKHR[] Formats;
        public PresentModeKHR[] PresentModes;
    }

    public unsafe class VkLogicalDevice : VkObject<Device>
    {
        private readonly Vk _api;
        private readonly VkQueue _presentQueue;
        private readonly VkQueue _graphicsQueue;
        private readonly VkQueue _computeQueue;
        private readonly VkPhysicalDevice _physicalDevice;
        private readonly QueueFamilyIndices _queueFamilyIndices;

        public VkQueue PresentQueue => _presentQueue;

        public VkQueue GraphicsQueue => _graphicsQueue;

        public VkPhysicalDevice PhysicalDevice => _physicalDevice;
        public VkQueue ComputeQueue => _computeQueue;

        public QueueFamilyIndices QueueFamilyIndices => _queueFamilyIndices;


        private VkLogicalDevice(Vk api, Device device, VkQueue graphicsQueue, VkQueue presentQueue, VkQueue computeQueue, QueueFamilyIndices indices, VkPhysicalDevice physicalDevice)
            : base(device)
        {
            _api = api;
            _graphicsQueue = graphicsQueue;
            _presentQueue = presentQueue;
            _computeQueue = computeQueue;
            _queueFamilyIndices = indices;
            _physicalDevice = physicalDevice;
           
        }
        internal void NameQueues()
        {
            _computeQueue.LabelObject("Compute Queue");
            _presentQueue.LabelObject("Present Queue");
            _graphicsQueue.LabelObject("Graphics Queue");
        }


        protected override unsafe void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                if (_vkObject.Handle != nint.Zero)
                {
                    _api.DestroyDevice(_vkObject, null);
                    _vkObject = default;
                }

                _disposed = true;
            }
        }



        public static VkLogicalDevice Create(VulkanContext context, VkPhysicalDevice physicalDevice, ISurfaceHandler surface, params string[] extensions)
        {
            var api = VulkanContext.Vk;
            // Find queue families
            QueueFamilyIndices indices = FindQueueFamilies(api, physicalDevice, surface);

            // Check if the device is suitable
            if (!IsDeviceSuitable(api, physicalDevice, surface, extensions, indices))
            {
                throw new Exception("Device is not suitable.");
            }

            // Create queue create info
            HashSet<uint> uniqueQueueFamilies = new HashSet<uint> { indices.GraphicsFamily!.Value, indices.PresentFamily!.Value, indices.ComputeFamily!.Value };
            var queueCreateInfos = stackalloc DeviceQueueCreateInfo[uniqueQueueFamilies.Count];
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

            var features2 = new PhysicalDeviceFeatures2();
            var vulkan11Features = new PhysicalDeviceVulkan11Features();
            var vulkan12Features = new PhysicalDeviceVulkan12Features();

            // Configure features
            features2.Features = new PhysicalDeviceFeatures
            {
                SamplerAnisotropy = true,
                DepthClamp = true,
                MultiDrawIndirect = true
            };

            vulkan11Features.SType = StructureType.PhysicalDeviceVulkan11Features;
            vulkan11Features.ShaderDrawParameters = true;

            vulkan12Features.SType = StructureType.PhysicalDeviceVulkan12Features;
            vulkan12Features.HostQueryReset = true;

            // Build feature chain using managed chaining
            using var chain = Chain.Create(
                features2,
                vulkan11Features,
                vulkan12Features
            );

            // Create device info with proper feature chain
            var deviceCreateInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = (uint)uniqueQueueFamilies.Count,
                PQueueCreateInfos = queueCreateInfos,
                PNext = chain.HeadPtr,
                EnabledExtensionCount = (uint)extensions.Length,
                PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(extensions)
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
            api.GetDeviceQueue(logicalDevice, indices.ComputeFamily.Value, 0, out Queue computeQueue);


            // Free unmanaged memory
            if (deviceCreateInfo.EnabledExtensionCount != 0)
            {
                SilkMarshal.Free((nint)deviceCreateInfo.PpEnabledExtensionNames);
            }
            SilkMarshal.Free((nint)deviceCreateInfo.PQueueCreateInfos);

            return new VkLogicalDevice(api, logicalDevice, new VkQueue(context, in graphicsQueue,indices.GraphicsFamily.Value), new VkQueue(context, in presentQueue, indices.PresentFamily.Value), new VkQueue(context, in computeQueue, indices.ComputeFamily.Value), indices, physicalDevice);
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
                    /*if (indices.ComputeFamily == null && i != indices.GraphicsFamily)
                    {*/
                        indices.ComputeFamily = i;
                    //}
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
            indices.ComputeFamily ??= indices.GraphicsFamily;

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
        public override void LabelObject(string name) => VulkanContext.GetCurrent().DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.Buffer, name);

    }
}