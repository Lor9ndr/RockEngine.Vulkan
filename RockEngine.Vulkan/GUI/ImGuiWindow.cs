using ImGuiNET;

using RockEngine.Vulkan.Rendering;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Input;
using Silk.NET.SDL;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;

using System.Runtime.InteropServices;

namespace RockEngine.Vulkan.GUI
{
    internal class ImGuiWindow : IAsyncDisposable
    {
        private readonly GCHandle _gcHandle;
        private readonly VulkanContext _context;
        private readonly ImGuiViewportPtr _vp;
        private readonly IWindow _window;
        private BaseRenderer _renderer;
        private bool _ready;
        private readonly IInputContext _inputContext;
        private readonly Task _task;

        public IWindow Window => _window;
        public BaseRenderer Render => _renderer;
        public Extent2D Extent => _renderer.Swapchain.Extent;
        public IInputContext Input => _inputContext;

        public bool Ready { get => _ready; set => _ready = value; }

        public unsafe ImGuiWindow(VulkanContext context, ImGuiViewportPtr vp)
        {
            _gcHandle = GCHandle.Alloc(this);
            _context = context;
            _vp = vp;
            _window = Silk.NET.Windowing.Window.Create(WindowOptions.DefaultVulkan with { TopMost = vp.Flags.HasFlag(ImGuiViewportFlags.TopMost), WindowBorder = WindowBorder.Hidden});
            _window.Title = "RockEngine";
            _window.Initialize();
            _window.Resize += (s) => _vp.PlatformRequestResize = true;
            _window.Move += (s) => _vp.PlatformRequestMove = true;
            _window.Closing += () => _vp.PlatformRequestClose = true;
            _inputContext = _window.CreateInput();

            vp.PlatformUserData = (IntPtr)_gcHandle;
            var surface = SDLSurfaceHandler.CreateSurface(_window, context);
            _renderer = new BaseRenderer(_context, surface);
            _ready = true;
            
            _task = Task.Run(_window.Run);
        }

        public ImGuiWindow(VulkanContext context, ImGuiViewportPtr vp, IWindow window)
        {
            _gcHandle = GCHandle.Alloc(this);
            _context = context;
            _vp = vp;
            _window = window;
            vp.PlatformUserData = (IntPtr)_gcHandle;
        }

        public async ValueTask DisposeAsync()
        {
            _context.Api.DeviceWaitIdle(_context.Device);

            _window.Close();
            await _task.ConfigureAwait(false);
            _renderer.Dispose();
            _window.Close();
            _gcHandle.Free();
        }
    }
}
