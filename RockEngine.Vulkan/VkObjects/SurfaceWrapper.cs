using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace RockEngine.Vulkan.VkObjects
{
    public class SurfaceWrapper : VkObject
    {
        private readonly Vk _api;
        private readonly KhrSurface _khrSurface;
        private readonly InstanceWrapper _instance;
        private SurfaceKHR _surface;
        public SurfaceKHR Surface => _surface;
        public KhrSurface SurfaceApi => _khrSurface;

        public SurfaceWrapper(Vk api, InstanceWrapper instance, SurfaceKHR surface, KhrSurface khrSurfaceApi)
        {
            _api = api;
            _instance = instance;
            _surface = surface;
            _khrSurface = khrSurfaceApi;
        }

        protected unsafe override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects) if any.
                }

                // Free unmanaged resources (unmanaged objects) and override a finalizer below.
                // Set large fields to null.
                if (_surface.Handle != 0)
                {
                    _khrSurface.DestroySurface(_instance.Instance, Surface, null);
                    _surface = default;
                }

                _disposed = true;
            }
        }

        // Override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        ~SurfaceWrapper()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }
    }
}