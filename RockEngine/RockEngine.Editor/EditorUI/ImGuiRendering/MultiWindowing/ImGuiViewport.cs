using ImGuiNET;

using RockEngine.Core;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Vulkan;

using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;

using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.InteropServices;

using static RockEngine.Editor.EditorUI.ImGuiRendering.ImGuiController;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.MultiWindowing
{
    public class RckImGuiViewport
    {
        internal SwapchainRenderTarget RenderTarget;

        public IWindow Window { get; }
        public ImGuiViewportPtr ViewportPtr { get; }
        public bool IsMainViewport { get; }
        public IInputContext InputContext { get; }
       
        public bool IsFocused  = true;
        public bool IsMinimized { get; private set; }

        public RckImGuiViewport(IWindow window, ImGuiViewportPtr viewportPtr, IInputContext inputContext, ImGuiViewportManager imGuiViewportManager, bool isMainViewport = false)
        {
            Window = window;
            ViewportPtr = viewportPtr;
            InputContext = inputContext;
            IsMainViewport = isMainViewport;
            Window.FocusChanged += (isFocused) =>
            {
                IsFocused = isFocused;
            };
        }
    }

    public unsafe class ImGuiViewportManager
    {
        private readonly VulkanContext _vkContext;
        private readonly GraphicsContext _graphicsContext;
        private readonly RckRenderPass _renderPass;
        private readonly ImGuiController _controller;
        private readonly Application _application;
        private readonly List<RckImGuiViewport> _viewports = new List<RckImGuiViewport>();
        private readonly Dictionary<uint, RckImGuiViewport> _viewportMap = new Dictionary<uint, RckImGuiViewport>();

        // Delegate definitions
        public delegate void Platform_CreateWindow(ImGuiViewportPtr vp);                    
        public delegate void Platform_DestroyWindow(ImGuiViewportPtr vp);
        public delegate void Platform_ShowWindow(ImGuiViewportPtr vp);                      
        public delegate void Platform_SetWindowPos(ImGuiViewportPtr vp, Vector2 pos);
        public delegate void Platform_GetWindowPos(ImGuiViewportPtr vp, out Vector2 outPos);
        public delegate void Platform_SetWindowSize(ImGuiViewportPtr vp, Vector2 size);
        public delegate void Platform_GetWindowSize(ImGuiViewportPtr vp, out Vector2 outSize);
        public delegate void Platform_SetWindowFocus(ImGuiViewportPtr vp);                  
        public delegate byte Platform_GetWindowFocus(ImGuiViewportPtr vp);
        public delegate byte Platform_GetWindowMinimized(ImGuiViewportPtr vp);
        public delegate void Platform_SetWindowTitle(ImGuiViewportPtr vp, IntPtr title);
        public delegate void Platform_CreateVkSurface(ImGuiViewportPtr vp, uint vkInstance, void* allocator, uint* surface);

        // Delegates to keep alive
        private readonly Platform_CreateWindow _createWindow;
        private readonly Platform_DestroyWindow _destroyWindow;
        private readonly Platform_GetWindowPos _getWindowPos;
        private readonly Platform_ShowWindow _showWindow;
        private readonly Platform_SetWindowPos _setWindowPos;
        private readonly Platform_SetWindowSize _setWindowSize;
        private readonly Platform_GetWindowSize _getWindowSize;
        private readonly Platform_SetWindowFocus _setWindowFocus;
        private readonly Platform_GetWindowFocus _getWindowFocus;
        private readonly Platform_GetWindowMinimized _getWindowMinimized;
        private readonly Platform_SetWindowTitle _setWindowTitle;

        private readonly Renderer_CreateWindow _rendererCreateWindow;
        private readonly Renderer_DestroyWindow _rendererDestroyWindow;
        private readonly Renderer_SetWindowSize _rendererSetWindowSize;
        private readonly Renderer_RenderWindow _rendererRenderWindow;
        private readonly Renderer_SwapBuffers _rendererSwapBuffers;

        public delegate void Renderer_CreateWindow(ImGuiViewportPtr vp);
        public delegate void Renderer_DestroyWindow(ImGuiViewportPtr vp);
        public delegate void Renderer_SetWindowSize(ImGuiViewportPtr vp, Vector2 size);
        public delegate void Renderer_RenderWindow(ImGuiViewportPtr vp, nint renderArg);
        public delegate void Renderer_SwapBuffers(ImGuiViewportPtr vp, void* renderArg);

        public IReadOnlyList<RckImGuiViewport> Viewports => _viewports;
        public RckImGuiViewport MainViewport => _viewports.FirstOrDefault(v => v.IsMainViewport);

        public ImGuiViewportManager(VulkanContext vkContext, GraphicsContext graphicsContext, ImGuiController controller, RckRenderPass renderPass, Core.Application application)
        {
            _vkContext = vkContext;
            _graphicsContext = graphicsContext;
            _controller = controller;
            _application = application ?? throw new ArgumentNullException(nameof(application));
            _renderPass = renderPass;
            var io = ImGui.GetIO();
            // Get platform IO
            var platformIO = ImGui.GetPlatformIO();
            if (io.BackendFlags.HasFlag(ImGuiBackendFlags.PlatformHasViewports))
            {
                _createWindow = CreateWindow;
                _destroyWindow = DestroyWindow;
                _showWindow = ShowWindow;
                _setWindowPos = SetWindowPos;
                _getWindowPos = GetWindowPos;
                _setWindowSize = SetWindowSize;
                _getWindowSize = GetWindowSize;
                _setWindowFocus = SetWindowFocus;
                _getWindowFocus = GetWindowFocus;
                _getWindowMinimized = GetWindowMinimized;
                _setWindowTitle = SetWindowTitle;
                // Set platform callbacks
                platformIO.Platform_CreateWindow = Marshal.GetFunctionPointerForDelegate(_createWindow);
                platformIO.Platform_DestroyWindow = Marshal.GetFunctionPointerForDelegate(_destroyWindow);
                platformIO.Platform_ShowWindow = Marshal.GetFunctionPointerForDelegate(_showWindow);
                platformIO.Platform_SetWindowPos = Marshal.GetFunctionPointerForDelegate(_setWindowPos);
                platformIO.Platform_SetWindowSize = Marshal.GetFunctionPointerForDelegate(_setWindowSize);
                platformIO.Platform_SetWindowFocus = Marshal.GetFunctionPointerForDelegate(_setWindowFocus);
                platformIO.Platform_GetWindowFocus = Marshal.GetFunctionPointerForDelegate(_getWindowFocus);
                platformIO.Platform_GetWindowMinimized = Marshal.GetFunctionPointerForDelegate(_getWindowMinimized);
                platformIO.Platform_SetWindowTitle = Marshal.GetFunctionPointerForDelegate(_setWindowTitle);
                ImGuiNative.ImGuiPlatformIO_Set_Platform_GetWindowPos(platformIO.NativePtr, Marshal.GetFunctionPointerForDelegate(_getWindowPos));
                ImGuiNative.ImGuiPlatformIO_Set_Platform_GetWindowSize(platformIO.NativePtr, Marshal.GetFunctionPointerForDelegate(_getWindowSize));
            }



            if (io.BackendFlags.HasFlag(ImGuiBackendFlags.RendererHasViewports))
            {
                _rendererCreateWindow = RendererCreateWindow;
                _rendererDestroyWindow = RendererDestroyWindow;
                _rendererRenderWindow = RendererRenderWindow;
                _rendererSwapBuffers = RendererSwapBuffers;
                platformIO.Renderer_CreateWindow = Marshal.GetFunctionPointerForDelegate(_rendererCreateWindow);
                platformIO.Renderer_DestroyWindow = Marshal.GetFunctionPointerForDelegate(_rendererDestroyWindow);
                platformIO.Renderer_RenderWindow = Marshal.GetFunctionPointerForDelegate(_rendererRenderWindow);
                platformIO.Platform_SwapBuffers = Marshal.GetFunctionPointerForDelegate(_rendererSwapBuffers);
            }

            //platformIO.Renderer_SwapBuffers = Marshal.GetFunctionPointerForDelegate(_rendererSwapBuffers);

          
        }


        public void RegisterMainViewport(IWindow mainWindow, IInputContext mainInputContext)
        {
            var mainViewport = ImGui.GetMainViewport();
            var viewport = new RckImGuiViewport(mainWindow, mainViewport, mainInputContext, this, true);

            mainViewport.PlatformUserData = mainWindow.Handle;
            mainViewport.PlatformHandle = mainWindow.Handle;
            mainViewport.PlatformHandleRaw = mainWindow.Handle;

            _viewports.Add(viewport);
            _viewportMap[mainViewport.ID] = viewport;

            Console.WriteLine($"Registered main viewport with handle: {mainWindow.Handle}");
        }

        private void CreateWindow(ImGuiViewportPtr viewportPtr)
        {
            try
            {

                var windowOptions = WindowOptions.DefaultVulkan with
                {
                    IsContextControlDisabled = true,
                    //IsEventDriven = true,
                    IsVisible = true,
                    WindowBorder = WindowBorder.Hidden,
                };

                var window = _graphicsContext.MainSwapchain.Surface.Window.CreateWindow(windowOptions);
                window.Initialize();

                var inputContext = window.CreateInput();

                var viewport = new RckImGuiViewport(window, viewportPtr, inputContext,this);
               
                _viewports.Add(viewport);
                _viewportMap[viewportPtr.ID] = viewport;


                // Store window handle
                viewportPtr.PlatformUserData = window.Handle;
                viewportPtr.PlatformHandle = window.Handle;
                viewportPtr.PlatformHandleRaw = window.Handle;
                viewportPtr.PlatformWindowCreated = true;

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create ImGui viewport window: {ex.Message}");
            }
        }
        private void SetWindowAlpha(ImGuiViewportPtr vp, float alpha)
        {
            if (_viewportMap.TryGetValue(vp.ID, out var viewport))
            {
                try
                {
                    
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SetWindowAlpha error for viewport {vp.ID}: {ex.Message}");
                }
            }
        }

        private void DestroyWindow(ImGuiViewportPtr viewportPtr)
        {
            if (_viewportMap.TryGetValue(viewportPtr.ID, out var viewport))
            {
                try
                {
                    // Clear user data BEFORE destroying the window
                    viewportPtr.PlatformUserData = IntPtr.Zero;
                    viewportPtr.PlatformHandle = IntPtr.Zero;
                    viewportPtr.PlatformHandleRaw = IntPtr.Zero;
                    viewportPtr.RendererUserData = IntPtr.Zero; // Clear renderer data too

                    if (!viewport.IsMainViewport)
                    {
                        viewport.InputContext?.Dispose();
                        viewport.Window.Close();
                        viewport.Window.Dispose();
                    }

                    _viewports.Remove(viewport);
                    _viewportMap.Remove(viewportPtr.ID);

                    Console.WriteLine($"Destroyed ImGui viewport window: {viewport.Window.Handle}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error destroying viewport window: {ex.Message}");
                }
            }
        }

        private void ShowWindow(ImGuiViewportPtr vp)
        {
            if (_viewportMap.TryGetValue(vp.ID, out var viewport))
            {
                viewport.Window.IsVisible = true;
            }
        }

        private void SetWindowPos(ImGuiViewportPtr vp, Vector2 pos)
        {
            if (_viewportMap.TryGetValue(vp.ID, out var viewport))
            {
                viewport.Window.Position = new Vector2D<int>((int)pos.X, (int)pos.Y);
            }
        }

        private void GetWindowPos(ImGuiViewportPtr vp, out Vector2 outPos)
        {
            if (_viewportMap.TryGetValue(vp.ID, out var viewport))
            {
                var pos = viewport.Window.Position;
                outPos =  new Vector2(pos.X, pos.Y);
                return;
            }
            outPos = Vector2.Zero;
        }

        private void SetWindowSize(ImGuiViewportPtr vp, Vector2 size)
        {
            if (_viewportMap.TryGetValue(vp.ID, out var viewport))
            {
                viewport.Window.Size = new Vector2D<int>((int)size.X, (int)size.Y);
            }
        }

        private void GetWindowSize(ImGuiViewportPtr vp, out Vector2 outSize)
        {
            if (_viewportMap.TryGetValue(vp.ID, out var viewport))
            {
                var size = viewport.Window.Size;
                outSize =  new Vector2(size.X, size.Y);
                return;
            }
            outSize = Vector2.Zero;
        }

        private void SetWindowFocus(ImGuiViewportPtr vp)
        {
            if (_viewportMap.TryGetValue(vp.ID, out var viewport))
            {
                viewport.Window.Focus();
            }
        }

        private byte GetWindowFocus(ImGuiViewportPtr vp)
        {
            if (_viewportMap.TryGetValue(vp.ID, out var viewport))
            {
                return viewport.IsFocused ? (byte)1 : (byte)0;
            }
            return 0;
        }

        private byte GetWindowMinimized(ImGuiViewportPtr vp)
        {
            if (_viewportMap.TryGetValue(vp.ID, out var viewport))
            {
                return viewport.Window.WindowState == WindowState.Minimized ? (byte)1 : (byte)0;
            }
            return 0;
        }

        private void SetWindowTitle(ImGuiViewportPtr vp, IntPtr title)
        {
            if (_viewportMap.TryGetValue(vp.ID, out var viewport))
            {
                var titleString = Marshal.PtrToStringUTF8(title);
                if (titleString != null)
                {
                    viewport.Window.Title = titleString;
                }
            }
        }

        private void RenderWindow(ImGuiViewportPtr vp, void* renderArg)
        {
            // Handled by ImGui.RenderPlatformWindowsDefault()
        }

        private bool IsWindowFocused(RckImGuiViewport viewport)
        {
            return viewport.IsFocused;
        }

        private void RendererCreateWindow(ImGuiViewportPtr vp)
        {
            if (_viewportMap.TryGetValue(vp.ID, out var viewport))
            {
                try
                {
                    // Create render context for this viewport
                    var surface = SurfaceHandler.CreateSurface(viewport.Window, _vkContext);
                    var swapchain = VkSwapchain.Create(_vkContext, surface);

                    var renderTarget = new SwapchainRenderTarget(_vkContext, swapchain);
                    renderTarget.Initialize(_renderPass);

                    // Add to graphics context
                    _graphicsContext.AddSwapchain(swapchain);

                    viewport.RenderTarget = renderTarget;
                    vp.RendererUserData = GCHandle.ToIntPtr(GCHandle.Alloc(renderTarget));

                    // CRITICAL: Ensure swapchain images are in correct layout
                    // The swapchain.Create method should handle this, but we'll ensure it here too
                    var batch = _vkContext.GraphicsSubmitContext.CreateBatch();

                    Span<ImageMemoryBarrier2> barriers = stackalloc ImageMemoryBarrier2[swapchain.SwapChainImagesCount];
                    for (int i = 0; i < swapchain.SwapChainImagesCount; i++)
                    {
                        var image = swapchain.VkImages[i];
                        var barrier = new ImageMemoryBarrier2
                        {
                            SType = StructureType.ImageMemoryBarrier2,
                            OldLayout = ImageLayout.Undefined,
                            NewLayout = ImageLayout.PresentSrcKhr,
                            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                            Image = image.VkObjectNative,
                            SubresourceRange = new ImageSubresourceRange
                            {
                                AspectMask = ImageAspectFlags.ColorBit,
                                BaseMipLevel = 0,
                                LevelCount = 1,
                                BaseArrayLayer = 0,
                                LayerCount = 1
                            },
                            SrcAccessMask = AccessFlags2.None,
                            DstAccessMask = AccessFlags2.ColorAttachmentWriteBit,
                            SrcStageMask = PipelineStageFlags2.None,
                            DstStageMask = PipelineStageFlags2.ColorAttachmentOutputBit,

                        };
                        barriers[i] = barrier;



                    }
                    batch.PipelineBarrier([], [], barriers);

                    using var fence = VkFence.CreateNotSignaled(_vkContext);
                    _vkContext.GraphicsSubmitContext.SubmitSingle(batch, fence).Wait();
                    fence.Wait();

                    Console.WriteLine($"Created render resources for viewport {vp.ID}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to create render resources for viewport: {ex.Message}");
                }
            }
        }
        private void RendererDestroyWindow(ImGuiViewportPtr vp)
        {
            if (_viewportMap.TryGetValue(vp.ID, out var viewport))
            {
                    if (vp.RendererUserData != IntPtr.Zero)
                {
                    try
                    {
                        var handle = GCHandle.FromIntPtr(vp.RendererUserData);
                        var renderTarget = (SwapchainRenderTarget)handle.Target;

                        // Remove from graphics context
                        _graphicsContext.RemoveSwapchain(renderTarget.Swapchain);
                        _controller.CleanupViewportBuffers(viewport);

                        renderTarget.Dispose();
                        handle.Free();
                        vp.RendererUserData = IntPtr.Zero;


                        Console.WriteLine($"Destroyed render resources for viewport {vp.ID}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error destroying render resources: {ex.Message}");
                    }
                }
            }
        }

        private void RendererRenderWindow(ImGuiViewportPtr vp, nint renderArg)
        {
            if (vp.RendererUserData == IntPtr.Zero || vp.DrawData.CmdListsCount == 0)
                return;

            try
            {
                var handle = GCHandle.FromIntPtr(vp.RendererUserData);
                var renderContext = (SwapchainRenderTarget)handle.Target;

                // Check if viewport is valid and visible
                if (!_viewportMap.TryGetValue(vp.ID, out var rckViewport) || vp.Size.X <= 0 || vp.Size.Y <= 0)
                    return;

                try
                {
                   
                    if (renderContext.Swapchain.CurrentImageIndex == uint.MaxValue)
                    {
                        return;
                    }
                    var gcHandle = GCHandle.FromIntPtr(renderArg);
                    var vpImgui = (ViewportImguiStruct)gcHandle.Target;
                    // Get current image index from swapchain
                    uint imageIndex = vpImgui.FrameIndex;

                    _controller.RenderImDrawData(vp.DrawData, renderContext, vpImgui.Batch, vpImgui.FrameIndex, rckViewport);
                   
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error rendering viewport {vp.ID}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CRITICAL: Render error in viewport {vp.ID}: {ex.Message}");
            }
        }

        private void RendererSwapBuffers(ImGuiViewportPtr vp, void* renderArg)
        {
            if (vp.RendererUserData == IntPtr.Zero)
                return;

            try
            {
                var handle = GCHandle.FromIntPtr(vp.RendererUserData);
                var renderContext = (SwapchainRenderTarget)handle.Target;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CRITICAL: Present error in viewport {vp.ID}: {ex.Message}");
            }
        }

        public void Dispose()
        {
            foreach (var viewport in _viewports.ToArray())
            {
                if (!viewport.IsMainViewport)
                {
                    try
                    {
                        DestroyWindow(viewport.ViewportPtr);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error disposing viewport {viewport.ViewportPtr.ID}: {ex.Message}");
                    }
                }
            }
            _viewports.Clear();
            _viewportMap.Clear();
        }
    }
}