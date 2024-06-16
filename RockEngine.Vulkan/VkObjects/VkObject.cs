namespace RockEngine.Vulkan.VkObjects
{
    public abstract class VkObject<T> : IDisposable
    {
        protected T _vkObject;
        protected bool _disposed;
        public T VkObjectNative =>_vkObject;

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
            GC.SuppressFinalize(this);
        }
        public static implicit operator T(VkObject<T> value) => value._vkObject;
    }
}
