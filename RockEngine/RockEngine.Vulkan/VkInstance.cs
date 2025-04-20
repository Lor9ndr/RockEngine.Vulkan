using Silk.NET.Vulkan;

using System.Runtime.InteropServices;

namespace RockEngine.Vulkan
{
    public class VkInstance : VkObject<Instance>
    {
        public DebugUtilsMessengerEXT? DebugMessenger { get; set; }

        public VkInstance(Instance instance)
            : base(instance)
        {
        }


        protected override unsafe void Dispose(bool disposing)
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
                        var destroyDebugUtils = VulkanContext.Vk.GetInstanceProcAddr(_vkObject, "vkDestroyDebugUtilsMessengerEXT");
                        var del = Marshal.GetDelegateForFunctionPointer<DestroyDebugUtilsDelegate>(destroyDebugUtils);
                        del(_vkObject, DebugMessenger.Value, default);

                    }

                    VulkanContext.Vk.DestroyInstance(_vkObject, in VulkanContext.CustomAllocator<VkInstance>());

                    _vkObject = default;
                }

                _disposed = true;
            }
        }
        private unsafe delegate void DestroyDebugUtilsDelegate(Instance instance, DebugUtilsMessengerEXT messenger, AllocationCallbacks* pAllocator);
    }
}