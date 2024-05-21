using Silk.NET.Vulkan;

using System.Runtime.InteropServices;

namespace RockEngine.Vulkan.VkObjects
{
    public class VulkanInstance : VkObject
    {
        private Instance _instance;
        private readonly Vk _api;
        public DebugUtilsMessengerEXT? DebugMessenger { get; set; }

        public VulkanInstance(Instance instance, Vk api)
        {
            _instance = instance;
            _api = api;
        }

        public Instance Instance => _instance;

        protected unsafe override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                // Free unmanaged resources (unmanaged objects) and override finalizer
                if (_instance.Handle != nint.Zero)
                {
                    if (DebugMessenger.HasValue)
                    {
                        var destroyDebugUtils = _api.GetInstanceProcAddr(_instance, "vkDestroyDebugUtilsMessengerEXT");
                        var del = Marshal.GetDelegateForFunctionPointer<DestroyDebugUtilsDelegate>(destroyDebugUtils);
                        del(_instance, DebugMessenger.Value, null);
                    }

                    _api.DestroyInstance(_instance, null);
                    
                    _instance = default;
                }

                _disposed = true;
            }
        }
        private unsafe delegate void DestroyDebugUtilsDelegate(Instance instance, DebugUtilsMessengerEXT messenger, AllocationCallbacks* pAllocator);
    }
}