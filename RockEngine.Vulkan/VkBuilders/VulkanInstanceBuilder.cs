using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;

using System.Runtime.InteropServices;
using System.Text;

namespace RockEngine.Vulkan.VkBuilders
{
    public class VulkanInstanceBuilder
    {
        public const string CREATE_DEBUG_UTILS_MESSENGER = "vkCreateDebugUtilsMessengerEXT";
        private bool _enableValidationLayers;
        private string[]? _validationLayers;
        private readonly Vk _api;
        private DebugUtilsMessengerCreateInfoEXT? _debugUtilsMessengerCreateInfoEXT;

        public VulkanInstanceBuilder(Vk api)
        {
            _api = api;
        }
        public VulkanInstanceBuilder UseValidationLayers(string[] validationLayers)
        {
            _enableValidationLayers = true;
            _validationLayers = validationLayers;
            return this;
        }
        public unsafe VulkanInstanceBuilder UseDebugUtilsMessenger(DebugUtilsMessageSeverityFlagsEXT severity,
            DebugUtilsMessageTypeFlagsEXT type,
            PfnDebugUtilsMessengerCallbackEXT userCallback,
            void* userData)
        {
            _debugUtilsMessengerCreateInfoEXT = new DebugUtilsMessengerCreateInfoEXT()
            {
                SType = StructureType.DebugUtilsMessengerCreateInfoExt,
                MessageSeverity = severity,
                MessageType = type,
                PfnUserCallback = userCallback,
                PUserData = userData,
            };
            return this;
        }

        public unsafe InstanceWrapper Build(ref InstanceCreateInfo instanceInfo)
        {
            if (_enableValidationLayers && !CheckValidationLayerSupport(_api))
            {
                throw new Exception("Validation layers requested, but not available!");
            }
            byte** validationLayerNames = null;
            // Add logic to modify instanceCreateInfo based on validation layers and extensions
            if (_enableValidationLayers)
            {
                if (instanceInfo.EnabledLayerCount != 0)
                {
                    foreach (var layer in _validationLayers!)
                    {
                        instanceInfo.PpEnabledLayerNames = UnmanagedExtensions.AddToStringArray(instanceInfo.PpEnabledLayerNames, instanceInfo.EnabledLayerCount, layer, Encoding.UTF8);
                        instanceInfo.EnabledLayerCount++;
                    }
                }
                else
                {
                    validationLayerNames = _validationLayers!.ToUnmanagedArray(Encoding.UTF8);
                    instanceInfo.EnabledLayerCount = (uint)_validationLayers!.Length;
                }

                instanceInfo.PpEnabledLayerNames = validationLayerNames;

                // Add the VK_EXT_debug_utils extension.
                byte** newExtensions = UnmanagedExtensions.AddToStringArray(instanceInfo.PpEnabledExtensionNames,
                    instanceInfo.EnabledExtensionCount, "VK_EXT_debug_utils", Encoding.UTF8);
                instanceInfo.EnabledExtensionCount += 1; // Update count to reflect the new size
                instanceInfo.PpEnabledExtensionNames = newExtensions;
            }
            if (_debugUtilsMessengerCreateInfoEXT.HasValue)
            {
                var value = _debugUtilsMessengerCreateInfoEXT.Value;
                instanceInfo.PNext = &value;
            }
            InstanceWrapper instanceWrapper;

            var result = _api.CreateInstance(in instanceInfo, null, out Instance instance);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to create instance: {result}");
            }
            instanceWrapper = new InstanceWrapper(instance, _api);
            if (validationLayerNames != null)
            {
                UnmanagedExtensions.FreeUnmanagedArray(validationLayerNames, _validationLayers!.Length);
            }
            if (_debugUtilsMessengerCreateInfoEXT.HasValue)
            {
                var rslt = CreateDebugUtilsMessenger(_api, instanceWrapper, _debugUtilsMessengerCreateInfoEXT.Value, out var messenger);
                if (rslt != Result.Success)
                {
                    throw new Exception("Unable to create debug utils messenger");
                }
                instanceWrapper.DebugMessenger = messenger;
            }
            
            Marshal.FreeHGlobal((nint)instanceInfo.PpEnabledExtensionNames);

            return instanceWrapper;
        }
        unsafe Result CreateDebugUtilsMessenger(Vk api, InstanceWrapper instance, DebugUtilsMessengerCreateInfoEXT ci, out DebugUtilsMessengerEXT messenger)
        {
            nint vkCreateDebugUtilsMessengerEXTPtr = api.GetInstanceProcAddr(instance, CREATE_DEBUG_UTILS_MESSENGER);
            if (vkCreateDebugUtilsMessengerEXTPtr == nint.Zero)
            {
                throw new Exception("Failed to load vkCreateDebugUtilsMessengerEXT");
            }
            var del = Marshal.GetDelegateForFunctionPointer<CreateDebugUtilsMessengerDelegate>(vkCreateDebugUtilsMessengerEXTPtr);
            return del(instance, &ci, null, out messenger);
        }

        unsafe delegate Result CreateDebugUtilsMessengerDelegate(Instance instance, DebugUtilsMessengerCreateInfoEXT* ci, void* userData, out DebugUtilsMessengerEXT messenger);

        private unsafe bool CheckValidationLayerSupport(Vk api)
        {
            uint layerCount = 0;
            api.EnumerateInstanceLayerProperties(ref layerCount, null);

            LayerProperties[] availableLayers = new LayerProperties[layerCount];
            fixed (LayerProperties* pLayerProperties = availableLayers)
            {
                api.EnumerateInstanceLayerProperties(ref layerCount, pLayerProperties);
            }
            foreach (var validationLayer in _validationLayers!)
            {
                bool layerFound = false;
                foreach (var layerProperties in availableLayers)
                {
                    var str = Encoding.ASCII.GetString(layerProperties.LayerName, 256).Replace('\0', ' ').Trim();
                    if (str == validationLayer)
                    {
                        layerFound = true;
                        break;
                    }
                }
                if (!layerFound)
                {
                    return false;
                }
            }
            return true;
        }

    }
}