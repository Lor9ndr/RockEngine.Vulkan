
using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public abstract record VkObject<T> : IDisposable where T : struct
    {
        protected T _vkObject;
        protected bool _disposed;
        public T VkObjectNative => _vkObject;
        protected Vk Vk => VulkanContext.Vk;

        protected VkObject(in T vkObject)
        {
            _vkObject = vkObject;
        }

        protected abstract void Dispose(bool disposing);


        // Override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        ~VkObject()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            _vkObject = default;
            GC.SuppressFinalize(this);
        }
        public static implicit operator T(VkObject<T> value) => value._vkObject;

    }
}
