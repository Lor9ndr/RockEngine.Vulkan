using Silk.NET.Core.Contexts;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;

using System.Numerics;

namespace RockEngine.Vulkan
{
    public interface ISurfaceHandler : IDisposable
    {
        public SurfaceKHR Surface { get; }
        public KhrSurface SurfaceApi { get; }

        public IWindow Window { get;}
        public Vector2 Size { get; }

        public delegate void FramebufferResizeHandler(Vector2 size);

        public event FramebufferResizeHandler OnFramebufferResize;

    }
}
