using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan.Extensions.KHR;

using Silk.NET.Vulkan;

using System.Numerics;

public interface ISurfaceHandler: IDisposable
{
    public VulkanContext Context { get;}
    public SurfaceKHR Surface { get; }
    public KhrSurface SurfaceApi { get;}

    public Vector2 Size { get;}

    public delegate void FramebufferResizeHandler(Vector2 size);
    
    event FramebufferResizeHandler OnFramebufferResize;

}