using Silk.NET.Vulkan;

using System.Runtime.InteropServices;
using System.Text;

namespace RockEngine.Vulkan.Builders
{
    internal class VkInstanceBuilder
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Стили именования", Justification = "<Ожидание>")]
        private const string CREATE_DEBUG_UTILS_MESSENGER = "vkCreateDebugUtilsMessengerEXT";
        private bool _enableValidationLayers;
        private string[]? _validationLayers;
        private DebugUtilsMessengerCreateInfoEXT? _debugUtilsMessengerCreateInfoEXT;


        public VkInstanceBuilder UseValidationLayers(string[] validationLayers)
        {
            _enableValidationLayers = true;
            _validationLayers = validationLayers;
            return this;
        }
        public unsafe VkInstanceBuilder UseDebugUtilsMessenger(DebugUtilsMessageSeverityFlagsEXT severity,
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

        public unsafe VkInstance Build(ref InstanceCreateInfo instanceInfo)
        {
            if (_enableValidationLayers && !CheckValidationLayerSupport())
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
                        instanceInfo.PpEnabledLayerNames = AddToStringArray(instanceInfo.PpEnabledLayerNames, instanceInfo.EnabledLayerCount, layer, Encoding.UTF8);
                        instanceInfo.EnabledLayerCount++;
                    }
                }
                else
                {
                    validationLayerNames = ToUnmanagedArray(_validationLayers!, Encoding.UTF8);
                    instanceInfo.EnabledLayerCount = (uint)_validationLayers!.Length;
                }

                instanceInfo.PpEnabledLayerNames = validationLayerNames;

                // Add the VK_EXT_debug_utils extension.
                byte** newExtensions = AddToStringArray(instanceInfo.PpEnabledExtensionNames,
                    instanceInfo.EnabledExtensionCount, "VK_EXT_debug_utils", Encoding.UTF8);
                instanceInfo.EnabledExtensionCount += 1; // Update count to reflect the new size
                instanceInfo.PpEnabledExtensionNames = newExtensions;
            }
            if (_debugUtilsMessengerCreateInfoEXT.HasValue)
            {
                var value = _debugUtilsMessengerCreateInfoEXT.Value;
                instanceInfo.PNext = &value;
            }
            VkInstance instanceWrapper;

            VulkanContext.Vk.CreateInstance(in instanceInfo, in CustomAllocator.CreateCallbacks<VkInstance>(), out Instance instance)
                .VkAssertResult($"Failed to create instance");

            instanceWrapper = new VkInstance(instance);
            if (validationLayerNames != null)
            {
                FreeUnmanagedArray(validationLayerNames, _validationLayers!.Length);
            }
            if (_debugUtilsMessengerCreateInfoEXT.HasValue)
            {
                var rslt = CreateDebugUtilsMessenger(instanceWrapper, _debugUtilsMessengerCreateInfoEXT.Value, out var messenger);
                if (rslt != Result.Success)
                {
                    throw new Exception("Unable to create debug utils messenger");
                }
                instanceWrapper.DebugMessenger = messenger;
            }

            Marshal.FreeHGlobal((nint)instanceInfo.PpEnabledExtensionNames);

            return instanceWrapper;
        }
        private unsafe Result CreateDebugUtilsMessenger(VkInstance instance, DebugUtilsMessengerCreateInfoEXT ci, out DebugUtilsMessengerEXT messenger)
        {
            nint vkCreateDebugUtilsMessengerEXTPtr = VulkanContext.Vk.GetInstanceProcAddr(instance, CREATE_DEBUG_UTILS_MESSENGER);
            if (vkCreateDebugUtilsMessengerEXTPtr == nint.Zero)
            {
                throw new Exception("Failed to load vkCreateDebugUtilsMessengerEXT");
            }
            var del = Marshal.GetDelegateForFunctionPointer<CreateDebugUtilsMessengerDelegate>(vkCreateDebugUtilsMessengerEXTPtr);
            return del(instance, &ci, null, out messenger);
        }

        unsafe delegate Result CreateDebugUtilsMessengerDelegate(Instance instance, DebugUtilsMessengerCreateInfoEXT* ci, void* userData, out DebugUtilsMessengerEXT messenger);

        private unsafe bool CheckValidationLayerSupport()
        {
            uint layerCount = 0;
            VulkanContext.Vk.EnumerateInstanceLayerProperties(ref layerCount, null);

            LayerProperties[] availableLayers = new LayerProperties[layerCount];
            fixed (LayerProperties* pLayerProperties = availableLayers)
            {
                VulkanContext.Vk.EnumerateInstanceLayerProperties(ref layerCount, pLayerProperties);
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
        /// <summary>
        /// Adds a new string to an existing unmanaged byte** array.
        /// </summary>
        /// <param name="originalArray">The original byte** array.</param>
        /// <param name="originalLength">The length of the original array.</param>
        /// <param name="newString">The new string to add.</param>
        /// <returns>A new byte** array containing the original data and the new string.</returns>
        public static unsafe byte** AddToStringArray(byte** originalArray, uint originalLength, string newString, Encoding encoding)
        {
            // Allocate unmanaged memory for the new array, which is one element larger.
            byte** newArray = (byte**)Marshal.AllocHGlobal(sizeof(byte*) * (int)(originalLength + 1));

            // Copy the original pointers to the new array.
            for (int i = 0; i < originalLength; i++)
            {
                newArray[i] = originalArray[i];
            }

            // Convert the new string to a null-terminated encoded byte array.
            byte[] newStringBytes = encoding.GetBytes(newString + "\0");
            newArray[originalLength] = (byte*)Marshal.AllocHGlobal(newStringBytes.Length);
            Marshal.Copy(newStringBytes, 0, (nint)newArray[originalLength], newStringBytes.Length);

            // Return the new array. Remember to free the original array if it's no longer needed.
            return newArray;
        }

        /// <summary>
        /// Converts an array of strings to an unmanaged array of pointers to null-terminated UTF-8 encoded byte arrays.
        /// </summary>
        /// <param name="array">The array of strings to convert.</param>
        /// <returns>A pointer to the first element of an array of pointers to byte arrays.</returns>
        public static unsafe byte** ToUnmanagedArray(string[] array, Encoding encoding)
        {
            ArgumentNullException.ThrowIfNull(array);

            // Allocate unmanaged memory for the array of pointers. Each pointer will point to a null-terminated UTF-8 encoded byte array.
            byte** unmanagedArray = (byte**)Marshal.AllocHGlobal(array.Length * sizeof(byte*));

            for (int i = 0; i < array.Length; i++)
            {
                // Convert each string to a null-terminated UTF-8 encoded byte array.
                byte[] bytes = encoding.GetBytes(array[i] + "\0");

                // Allocate unmanaged memory for the byte array and copy the data.
                unmanagedArray[i] = (byte*)Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, (nint)unmanagedArray[i], bytes.Length);
            }

            return unmanagedArray;
        }

        /// <summary>
        /// Frees the unmanaged memory allocated by ToUnmanagedUtf8Array.
        /// </summary>
        /// <param name="unmanagedArray">The unmanaged array to free.</param>
        /// <param name="length">The length of the array.</param>
        public static unsafe void FreeUnmanagedArray(byte** unmanagedArray, int length)
        {
            ArgumentNullException.ThrowIfNull(unmanagedArray);

            for (int i = 0; i < length; i++)
            {
                // Free the unmanaged memory allocated for each byte array.
                Marshal.FreeHGlobal((nint)unmanagedArray[i]);
            }

            // Free the unmanaged memory allocated for the array of pointers.
            Marshal.FreeHGlobal((nint)unmanagedArray);
        }
    }
}
