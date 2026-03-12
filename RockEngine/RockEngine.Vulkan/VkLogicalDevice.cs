using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public ref struct SwapChainSupportDetails
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
        private readonly VkQueue _transferQueue; 
        private readonly VkPhysicalDevice _physicalDevice;
        private readonly QueueFamilyIndices _queueFamilyIndices;

        public VkQueue PresentQueue => _presentQueue;

        public VkQueue GraphicsQueue => _graphicsQueue;

        public VkQueue ComputeQueue => _computeQueue;

        public VkQueue TransferQueue => _transferQueue; 

        public VkPhysicalDevice PhysicalDevice => _physicalDevice;

        public QueueFamilyIndices QueueFamilyIndices => _queueFamilyIndices;


        private VkLogicalDevice(Vk api, Device device, VkQueue graphicsQueue, VkQueue presentQueue, VkQueue computeQueue, VkQueue transferQueue, QueueFamilyIndices indices, VkPhysicalDevice physicalDevice)
            : base(device)
        {
            _api = api;
            _graphicsQueue = graphicsQueue;
            _presentQueue = presentQueue;
            _computeQueue = computeQueue;
            _transferQueue = transferQueue;
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
            var registry = context.FeatureRegistry;
            if (!registry.CheckSupport(physicalDevice, out var unsupported))
            {
                // Handle unsupported required features – throw or disable them.
            }

            // Build the extension list: base extensions + registry extensions
            var allExtensions = new HashSet<string>(extensions);
            foreach (var ext in registry.GetAllRequiredExtensions())
                allExtensions.Add(ext);


            // Check if the device is suitable
            if (!IsDeviceSuitable(api, physicalDevice, surface, extensions, indices))
            {
                throw new Exception("Device is not suitable.");
            }

            // Create queue create info
            HashSet<uint> uniqueQueueFamilies = new HashSet<uint>
            {
                indices.GraphicsFamily!.Value,
                indices.PresentFamily!.Value,
                indices.ComputeFamily!.Value,
                indices.TransferFamily!.Value
            };
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
            var vk11 = new PhysicalDeviceVulkan11Features { SType = StructureType.PhysicalDeviceVulkan11Features };
            var vk12 = new PhysicalDeviceVulkan12Features { SType = StructureType.PhysicalDeviceVulkan12Features };
            var vk13 = new PhysicalDeviceVulkan13Features { SType = StructureType.PhysicalDeviceVulkan13Features };
            var pageableDeviceLocalMemoryFeatures = new PhysicalDevicePageableDeviceLocalMemoryFeaturesEXT{ SType = StructureType.PhysicalDevicePageableDeviceLocalMemoryFeaturesExt };

            // 6. Build feature chain
            uint extensionCount = 0;

            // 7. Let each enabled feature modify the structs / add to chain
            foreach (var feature in registry.Features.Where(f => registry.EnabledFeatures.Contains(f.Name)))
            {
                ///TODO: REWORK CHAIN PASSING
                using var chain2 = Chain.Create(features2, vk11, vk12, vk13, pageableDeviceLocalMemoryFeatures);
                if (feature.IsSupported(physicalDevice))
                {
                    feature.Enable(ref features2, ref vk11, ref vk12, ref vk13, ref pageableDeviceLocalMemoryFeatures, chain2);
                    extensionCount++;
                }
            }
            using var chain = Chain.Create(features2, vk11, vk12, vk13, pageableDeviceLocalMemoryFeatures);


            // Create device info with proper feature chain
            var deviceCreateInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = (uint)uniqueQueueFamilies.Count,
                PQueueCreateInfos = queueCreateInfos,
                PNext = chain.HeadPtr,
                EnabledExtensionCount = extensionCount,
                PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(extensions)
            };

            // Set extensions
            if (extensions != null && extensionCount > 0)
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
            api.GetDeviceQueue(logicalDevice, indices.TransferFamily.Value, 0, out Queue transferQueue);

            var x = api.IsDeviceExtensionPresent(context.Instance, "VK_EXT_pageable_device_local_memory");

            // Free unmanaged memory
            if (deviceCreateInfo.EnabledExtensionCount != 0)
            {
                SilkMarshal.Free((nint)deviceCreateInfo.PpEnabledExtensionNames);
            }
            SilkMarshal.Free((nint)deviceCreateInfo.PQueueCreateInfos);

            return new VkLogicalDevice(
                api, 
                logicalDevice, 
                new VkQueue(context, in graphicsQueue,indices.GraphicsFamily.Value),
                new VkQueue(context, in presentQueue, indices.PresentFamily.Value),
                new VkQueue(context, in computeQueue, indices.ComputeFamily.Value),
                new VkQueue(context, in transferQueue, indices.TransferFamily.Value),
                indices,
                physicalDevice);
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

                // Look for dedicated transfer queue (non-graphics/compute)
                if (queueFamilies[i].QueueFlags.HasFlag(QueueFlags.TransferBit) &&
                    !queueFamilies[i].QueueFlags.HasFlag(QueueFlags.GraphicsBit) &&
                    !queueFamilies[i].QueueFlags.HasFlag(QueueFlags.ComputeBit))
                {
                    indices.TransferFamily = i;
                }

                if (indices.IsComplete() && indices.TransferFamily.HasValue)
                {
                    break;
                }
            }

            // Fallbacks if dedicated transfer queue not found
            indices.ComputeFamily ??= indices.GraphicsFamily;
            indices.TransferFamily ??= indices.GraphicsFamily; // Fallback to graphics queue

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

        public void WaitIdle()
        {
            Vk.DeviceWaitIdle(this);
        }
    }
}