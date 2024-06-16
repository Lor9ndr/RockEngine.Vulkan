using Silk.NET.Vulkan;

using System.Runtime.InteropServices;

namespace RockEngine.Vulkan.VkObjects
{
    public class InstanceWrapper : VkObject<Instance>
    {
        private readonly Vk _api;
        public DebugUtilsMessengerEXT? DebugMessenger { get; set; }

        public InstanceWrapper(Instance instance, Vk api)
            :base(instance)
        {
            _api = api;
        }


        protected unsafe override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                // Free unmanaged resources (unmanaged objects) and override finalizer
                if (_vkObject.Handle != nint.Zero)
                {
                    if (DebugMessenger.HasValue)
                    {
                        var destroyDebugUtils = _api.GetInstanceProcAddr(_vkObject, "vkDestroyDebugUtilsMessengerEXT");
                        var del = Marshal.GetDelegateForFunctionPointer<DestroyDebugUtilsDelegate>(destroyDebugUtils);
                        del(_vkObject, DebugMessenger.Value, null);
                    }

                    _api.DestroyInstance(_vkObject, null);

                    _vkObject = default;
                }

                _disposed = true;
            }
        }
        private unsafe delegate void DestroyDebugUtilsDelegate(Instance instance, DebugUtilsMessengerEXT messenger, AllocationCallbacks* pAllocator);
    }
}