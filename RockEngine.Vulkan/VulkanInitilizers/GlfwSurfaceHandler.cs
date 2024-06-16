using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;

using static ISurfaceHandler;
using System.Numerics;
using RockEngine.Vulkan.VkObjects;

namespace RockEngine.Vulkan.VulkanInitilizers
{
    public class GlfwSurfaceHandler : VkObject<SurfaceKHR>, ISurfaceHandler
    {
        private readonly IWindow _window;
        private readonly VulkanContext _context;
        private KhrSurface _surfaceApi;
        private Vector2 _size;

        public VulkanContext Context => _context;


        public KhrSurface SurfaceApi => _surfaceApi;

        public Vector2 Size => _size;

        public SurfaceKHR Surface => _vkObject;

        public event FramebufferResizeHandler? OnFramebufferResize;

        public GlfwSurfaceHandler(IWindow window, VulkanContext context, SurfaceKHR surface)
            :base(surface)
        {
            _window = window;
            _context = context;
            _surfaceApi = new KhrSurface(_context.Api.Context);
            _size = GetSurfaceSize();
            _window.Resize += SurfaceResized;
        }

        public static unsafe GlfwSurfaceHandler CreateSurface(IWindow window, VulkanContext context)
        {
            if (window.VkSurface is null)
            {
                throw new Exception("Unable to create surface");
            }
            var handle = window.VkSurface.Create<AllocationCallbacks>(context.Instance.VkObjectNative.ToHandle(), null);
            var surface  = new SurfaceKHR(handle.Handle);
            return new GlfwSurfaceHandler(window, context, surface);
        }

        private unsafe Vector2 GetSurfaceSize()
        {
            Glfw.GetApi().GetFramebufferSize((WindowHandle*)_window.Handle, out var width, out var height);
            return new Vector2(width, height);

        }

        private void SurfaceResized(Vector2D<int> obj)
        {
            _size = new Vector2(obj.X, obj.Y);
            OnFramebufferResize?.Invoke(_size);
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
                if (_vkObject.Handle != 0)
                {
                    _window.Resize -= SurfaceResized;
                    _surfaceApi.Dispose();
                    _surfaceApi.DestroySurface(_context.Instance, _vkObject, null);
                    _vkObject = default;
                }

                _disposed = true;
            }
        }

        // Override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        ~GlfwSurfaceHandler()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }
    }
}
