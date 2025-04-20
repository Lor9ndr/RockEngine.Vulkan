using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;

using System.Numerics;

using static RockEngine.Vulkan.ISurfaceHandler;

namespace RockEngine.Vulkan
{
    internal class SDLSurfaceHandler : VkObject<SurfaceKHR>, ISurfaceHandler
    {
        private readonly IWindow _window;
        private readonly VulkanContext _context;
        private readonly KhrSurface _surfaceApi;
        private Vector2 _size;
        public IVkSurface? VkSurfaceNative { get; }
        public KhrSurface SurfaceApi => _surfaceApi;

        public Vector2 Size => _size;

        public SurfaceKHR Surface => _vkObject;

        public IWindow Window => _window;

        public event FramebufferResizeHandler? OnFramebufferResize;

        public SDLSurfaceHandler(IWindow window, VulkanContext context, in SurfaceKHR surface)
            : base(in surface)
        {
            VkSurfaceNative = window.VkSurface;
            _window = window;
            _context = context;
            _surfaceApi = new KhrSurface(VulkanContext.Vk.Context);
            _size = new Vector2(_window.Size.X, _window.Size.Y);
            _window.Resize += SurfaceResized;
        }

        public static unsafe SDLSurfaceHandler CreateSurface(IWindow window, VulkanContext context)
        {

            var handle = window.VkSurface.Create<AllocationCallbacks>(context.Instance.VkObjectNative.ToHandle(), default);
            var surface = new SurfaceKHR(handle.Handle);
            return new SDLSurfaceHandler(window, context, surface);


        }

        private void SurfaceResized(Vector2D<int> obj)
        {
            _size = new Vector2(obj.X, obj.Y);
            OnFramebufferResize?.Invoke(_size);
        }

        protected override unsafe void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects) if any.
                }

                // Free unmanaged resources (unmanaged objects) and override a finalizer below.
                // Set large fields to null.
                if (_vkObject.Handle != 0)
                {
                    _window.Resize -= SurfaceResized;
                    _surfaceApi.Dispose();
                    _surfaceApi.DestroySurface(_context.Instance, _vkObject, default);
                    _vkObject = default;
                }

                _disposed = true;
            }
        }


        // Override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        ~SDLSurfaceHandler()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }
    }
}
