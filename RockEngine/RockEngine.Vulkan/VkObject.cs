using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public abstract class VkObject<T> : IDisposable where T : struct
    {
        protected T _vkObject;
        protected bool _disposed;
        public T VkObjectNative => _vkObject;
        protected Vk Vk => VulkanContext.Vk;

        public bool IsDisposed { get => _disposed; protected set => _disposed = value; }

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
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Safely change vulkan object to new value without of disposing an <see cref="VkObject{T}"/>
        /// </summary>
        /// <param name="vkObject"></param>
        internal void InternalChangeVkObject(in T vkObject)
        {
            _vkObject = vkObject;
        }

        public abstract void LabelObject(string name);

        public static implicit operator T(VkObject<T> value) => value._vkObject;
    }
}
