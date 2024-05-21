namespace RockEngine.Vulkan.VkObjects
{
    public abstract class VkObject : IDisposable
    {
        protected bool _disposed;
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
    }
}
