using RockEngine.Vulkan.VkObjects;

using Silk.NET.Core.Native;
using Silk.NET.GLFW;

using Silk.NET.Maths;
using Silk.NET.SDL;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using static RockEngine.Vulkan.VulkanInitilizers.ISurfaceHandler;

using Window = Silk.NET.SDL.Window;

namespace RockEngine.Vulkan.VulkanInitilizers
{
    internal class SDLSurfaceHandler : VkObject<SurfaceKHR>, ISurfaceHandler
    {
        private readonly IView _window;
        private readonly VulkanContext _context;
        private readonly KhrSurface _surfaceApi;
        private Vector2 _size;

        public VulkanContext Context => _context;

        public KhrSurface SurfaceApi => _surfaceApi;

        public Vector2 Size => _size;

        public SurfaceKHR Surface => _vkObject;

        public event FramebufferResizeHandler? OnFramebufferResize;

        public SDLSurfaceHandler(IView window, VulkanContext context, SurfaceKHR surface)
            : base(surface)
        {
            _window = window;
            _context = context;
            _surfaceApi = new KhrSurface(_context.Api.Context);
            _size = GetSurfaceSize();
            _window.Resize += SurfaceResized;
        }

        public static unsafe SDLSurfaceHandler CreateSurface(IView window, VulkanContext context)
        {
            if (window.VkSurface is null)
            {
                VkNonDispatchableHandle surface = new VkNonDispatchableHandle();

                Sdl.GetApi().VulkanCreateSurface(SdlWindowing.GetHandle(window), context.Instance.VkObjectNative.ToHandle(), ref surface);
                return new SDLSurfaceHandler(window, context, new SurfaceKHR(surface.Handle));

            }
            else
            {
                var handle = window.VkSurface.Create<AllocationCallbacks>(context.Instance.VkObjectNative.ToHandle(), null);
                var surface = new SurfaceKHR(handle.Handle);
                return new SDLSurfaceHandler(window, context, surface);

            }

        }
        public static unsafe SDLSurfaceHandler CreateSurface(Window* window, VulkanContext context)
        {
            VkNonDispatchableHandle surface = new VkNonDispatchableHandle();
            var view = SdlWindowing.CreateFrom(window);

            Sdl.GetApi().VulkanCreateSurface(window, context.Instance.VkObjectNative.ToHandle(), ref surface);
            return new SDLSurfaceHandler(view, context, new SurfaceKHR(surface.Handle));


        }

        private unsafe Vector2 GetSurfaceSize()
        {
            return new Vector2(_window.Size.X, _window.Size.Y);
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
        ~SDLSurfaceHandler()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }
    }
}
