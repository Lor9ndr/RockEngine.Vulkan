using Silk.NET.Vulkan;

using System.Runtime.InteropServices;

namespace RockEngine.Vulkan
{
    public record VkInstance : VkObject<Instance>
    {
        public DebugUtilsMessengerEXT? DebugMessenger { get; set; }

        public VkInstance(Instance instance)
            : base(instance)
        {
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
                        var destroyDebugUtils = RenderingContext.Vk.GetInstanceProcAddr(_vkObject, "vkDestroyDebugUtilsMessengerEXT");
                        var del = Marshal.GetDelegateForFunctionPointer<DestroyDebugUtilsDelegate>(destroyDebugUtils);
                        del(_vkObject, DebugMessenger.Value, default);

                    }

                    RenderingContext.Vk.DestroyInstance(_vkObject, in RenderingContext.CustomAllocator<VkInstance>());

                    _vkObject = default;
                }

                _disposed = true;
            }
        }
        private unsafe delegate void DestroyDebugUtilsDelegate(Instance instance, DebugUtilsMessengerEXT messenger, AllocationCallbacks* pAllocator);
    }
}