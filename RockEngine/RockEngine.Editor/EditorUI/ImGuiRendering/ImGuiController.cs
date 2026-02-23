using ImGuiNET;

using RockEngine.Core;
using RockEngine.Core.Builders;
using RockEngine.Core.Diagnostics;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Editor.EditorUI.EditorWindows;
using RockEngine.Editor.EditorUI.ImGuiRendering.MultiWindowing;
using RockEngine.Editor.Rendering.Passes.SubPasses;
using RockEngine.Vulkan;

using Silk.NET.Input;
using Silk.NET.Input.Extensions;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;

using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RockEngine.Editor.EditorUI.ImGuiRendering
{
    public class ImGuiController : IDisposable
    {
        private const string ImguiRenderPass = "ImGuiPass";
        private readonly VulkanContext _vkContext;
        private readonly GraphicsContext _graphicsContext;
        private readonly InputManager _input;
        private VkPipelineLayout _pipelineLayout;
        private VkDescriptorSetLayout _descriptorSetLayout;
        private readonly VkBuffer[] _vertexBuffers;
        private readonly VkBuffer[] _indexBuffers;
        private bool _frameBegun;
        private bool _frameRendered = false;
        private readonly RckRenderPass _renderPass;
        private readonly BindingManager _bindingManager;
        private VkPipeline _pipeline;
        private readonly Queue<(IWindow window, char ch)> _pressedChars = new Queue<(IWindow, char)>();
        private readonly ulong _bufferMemoryAlignment;
        private Texture _fontTexture;
        private readonly Dictionary<Texture, TextureBinding> _textureBindings = new Dictionary<Texture, TextureBinding>();
        private readonly List<Texture> _texturesToRemove = new List<Texture>();
        private readonly Lock _textureCacheLock = new Lock();
        private bool _disposed;
        private readonly RenderTarget _uiRenderTarget;
        private ImFontPtr _iconFont;
        private bool _initialized;
        private TextureBinding _fontTextureBinding;

        private uint _currentFrame = 0;
        private ImGuiViewportManager _viewportManager;
        private nint _allocatedMonitorsData;
        private Vector2D<int> _lastMousePosition;

        public ImFontPtr IconFont { get => _iconFont; set => _iconFont = value; }

        private readonly Silk.NET.SDL.Sdl _windowingApi;
        private readonly Dictionary<RckImGuiViewport, (VkBuffer VertexBuffer, VkBuffer IndexBuffer)[]> _viewportBuffers = new();
        private readonly Lock _bufferLock = new();



        public unsafe ImGuiController(VulkanContext vkContext, GraphicsContext graphicsEngine,
                                      BindingManager bindingManager, InputManager inputContext, WorldRenderer renderer,
                                      IWindow mainWindow, Application application)
        {
            _vkContext = vkContext;
            _bindingManager = bindingManager;
            _graphicsContext = graphicsEngine;
            _input = inputContext;
            _uiRenderTarget = renderer.SwapchainTarget;
            _renderPass = _uiRenderTarget.RenderPass;

            // Create and set ImGui context
            var context = ImGui.CreateContext();
            ImGui.SetCurrentContext(context);

            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;

            //io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;
            //io.BackendFlags |= ImGuiBackendFlags.HasSetMousePos;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            io.BackendFlags |= ImGuiBackendFlags.PlatformHasViewports;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasViewports;

            // Initialize buffers first
            _vertexBuffers = new VkBuffer[_vkContext.MaxFramesPerFlight];
            _indexBuffers = new VkBuffer[_vkContext.MaxFramesPerFlight];

            for (int i = 0; i < _vkContext.MaxFramesPerFlight; i++)
            {
                _vertexBuffers[i] = VkBuffer.Create(_vkContext, 1024 * 1024, BufferUsageFlags.VertexBufferBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
                _indexBuffers[i] = VkBuffer.Create(_vkContext, 512 * 1024, BufferUsageFlags.IndexBufferBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            }

            // Set configuration
            _windowingApi = Silk.NET.Windowing.Sdl.SdlWindowing.GetExistingApi(mainWindow) ??
                throw new NotImplementedException("Imgui currently supported only on SDL windows");
            unsafe
            {
                io.NativePtr->BackendPlatformName = (byte*)new FixedAsciiString("RockEngine").DataPtr;
            }

            // Initialize device objects
            CreateDeviceObjects();
            CreateFontResources();
            CreateDescriptorSet();
            UpdateMonitors();

            // Set up input
            SetPerFrameImGuiData(1f / 60f);

            // Apply theme
            EditorTheme.ApplyModernDarkTheme();

            // Initialize viewport manager AFTER basic setup
            _viewportManager = new ImGuiViewportManager(_vkContext, _graphicsContext, this,_renderPass, application);
             _viewportManager.RegisterMainViewport(mainWindow, _input.Context);
            ImGui.NewFrame();
            _frameBegun = true;
            _initialized = true;
            renderer.GraphicsEngine.MainSwapchain.Surface.Window.StateChanged += (s) =>
            {

            };

        }

        private unsafe void UpdateMonitors()
        {
            try
            {
                // Free previous monitors data if it exists
                if (_allocatedMonitorsData != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_allocatedMonitorsData);
                    _allocatedMonitorsData = IntPtr.Zero;
                }

                var monitors = Silk.NET.Windowing.Monitor.GetMonitors(null).ToList();
                if (monitors == null || monitors.Count == 0)
                {
                    AddDefaultMonitor();
                    return;
                }

                var platformIO = ImGui.GetPlatformIO();

                int monitorCount = monitors.Count;
                int monitorSize = Unsafe.SizeOf<ImGuiPlatformMonitor>();
                int totalSize = monitorCount * monitorSize;

                // Allocate unmanaged memory for the monitors array
                IntPtr monitorsData = Marshal.AllocHGlobal(totalSize);

                for (int i = 0; i < monitorCount; i++)
                {
                    var monitor = monitors[i];
                    var imguiMonitor = new ImGuiPlatformMonitor
                    {
                        MainPos = new Vector2(monitor.Bounds.Origin.X, monitor.Bounds.Origin.Y),
                        MainSize = new Vector2(monitor.Bounds.Size.X, monitor.Bounds.Size.Y),
                        WorkPos = new Vector2(monitor.Bounds.Origin.X, monitor.Bounds.Origin.Y),
                        WorkSize = new Vector2(monitor.Bounds.Size.X, monitor.Bounds.Size.Y),
                        DpiScale = 1
                    };

                    Marshal.StructureToPtr(imguiMonitor, monitorsData + (i * monitorSize), false);
                }

                platformIO.NativePtr->Monitors = new ImVector(monitorCount, monitorCount, monitorsData);
                _allocatedMonitorsData = monitorsData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to update monitors: {ex.Message}");
                AddDefaultMonitor();
            }
        }

        private unsafe void AddDefaultMonitor()
        {
            var platformIO = ImGui.GetPlatformIO();

            // Create a default monitor with the main window dimensions
            int monitorSize = Unsafe.SizeOf<ImGuiPlatformMonitor>();
            IntPtr monitorData = Marshal.AllocHGlobal(monitorSize);

            var defaultMonitor = new ImGuiPlatformMonitor
            {
                MainPos = new Vector2(0, 0),
                MainSize = new Vector2(_graphicsContext.MainSwapchain.Surface.Size.X, _graphicsContext.MainSwapchain.Surface.Size.Y),
                WorkPos = new Vector2(0, 0),
                WorkSize = new Vector2(_graphicsContext.MainSwapchain.Surface.Size.X, _graphicsContext.MainSwapchain.Surface.Size.Y),
                DpiScale = 1.0f
            };

            Marshal.StructureToPtr(defaultMonitor, monitorData, false);
            platformIO.NativePtr->Monitors = new ImVector(1, 1, monitorData);

            _allocatedMonitorsData = monitorData;
        }



        private void UpdateImGuiInput()
        {
            var io = ImGui.GetIO();

            // Clear all keyboard state
            RckImGuiViewport mouseFocusedViewport = null;
            // Determine which viewport has mouse focus
            unsafe
            {
                var win = _windowingApi.GetMouseFocus();
                mouseFocusedViewport = _viewportManager.Viewports.FirstOrDefault(s => s.IsFocused);
            }

           

            // Update input for each viewport based on focus
            // Update keyboard input for the viewport that has keyboard focus
            if(mouseFocusedViewport is not null)
            {
                _input.SetInput(mouseFocusedViewport.Window, mouseFocusedViewport.InputContext);

                UpdateMainViewportInput(io, _input.Context);

                UpdateKeyboardInputForViewport(_input.Context, io);
                // Process pressed characters (window-specific)
                while (_pressedChars.Count > 0)
                {
                    var (window, ch) = _pressedChars.Dequeue();
                    io.AddInputCharacter(ch);
                }
                _pressedChars.Clear();
            }
        }
        
     
        private void UpdateKeyboardInputForViewport(IInputContext input, ImGuiIOPtr io)
        {
            var keyboardState = input.Keyboards.Count > 0 ? input.Keyboards[0] : null;

            if (keyboardState == null)
            {
                return;
            }
            // Update key states for this specific viewport
            foreach (Key key in keyboardState.SupportedKeys)
            {
                if (key == Key.Unknown) continue;

                if (TryMapKey(key, out ImGuiKey imguikey))
                {
                    io.AddKeyEvent(imguikey, keyboardState.IsKeyPressed(key));
                }
            }

            // Update modifier keys
            io.KeyCtrl = keyboardState.IsKeyPressed(Key.ControlLeft) || keyboardState.IsKeyPressed(Key.ControlRight);
            io.KeyAlt = keyboardState.IsKeyPressed(Key.AltLeft) || keyboardState.IsKeyPressed(Key.AltRight);
            io.KeyShift = keyboardState.IsKeyPressed(Key.ShiftLeft) || keyboardState.IsKeyPressed(Key.ShiftRight);
            io.KeySuper = keyboardState.IsKeyPressed(Key.SuperLeft) || keyboardState.IsKeyPressed(Key.SuperRight);
        }

        private void UpdateMainViewportInput(ImGuiIOPtr io, IInputContext input)
        {
            var mouseState = input.Mice.Count > 0 ? input.Mice[0] : null;

            if (mouseState != null)
            {
                var mousePos = new Vector2D<int>();
                
                _windowingApi.GetGlobalMouseState(ref mousePos.X, ref mousePos.Y);
                // Apply viewport offset if this is not the main window
                io.MousePos = new Vector2(mousePos.X, mousePos.Y);


                // Add mouse position event

                io.MouseDown[0] = mouseState.IsButtonPressed(MouseButton.Left);
                io.MouseDown[1] = mouseState.IsButtonPressed(MouseButton.Right);
                io.MouseDown[2] = mouseState.IsButtonPressed(MouseButton.Middle);


                // Update mouse wheel
                var scrollWheels = mouseState.ScrollWheels;
                if (scrollWheels.Count > 0)
                {
                    var wheel = scrollWheels[0];
                    io.MouseWheel = wheel.Y;
                    io.MouseWheelH = wheel.X;
                }
            }
        }

        private static bool TryMapKey(Key key, out ImGuiKey result)
        {
            static ImGuiKey KeyToImGuiKeyShortcut(Key keyToConvert, Key startKey1, ImGuiKey startKey2)
            {
                int changeFromStart1 = (int)keyToConvert - (int)startKey1;
                return startKey2 + changeFromStart1;
            }

            result = key switch
            {
                >= Key.F1 and <= Key.F24 => KeyToImGuiKeyShortcut(key, Key.F1, ImGuiKey.F1),
                >= Key.Keypad0 and <= Key.Keypad9 => KeyToImGuiKeyShortcut(key, Key.Keypad0, ImGuiKey.Keypad0),
                >= Key.A and <= Key.Z => KeyToImGuiKeyShortcut(key, Key.A, ImGuiKey.A),
                >= Key.Number0 and <= Key.Number9 => KeyToImGuiKeyShortcut(key, Key.Number0, ImGuiKey._0),
                Key.ShiftLeft or Key.ShiftRight => ImGuiKey.ModShift,
                Key.ControlLeft or Key.ControlRight => ImGuiKey.ModCtrl,
                Key.AltLeft or Key.AltRight => ImGuiKey.ModAlt,
                Key.SuperLeft or Key.SuperRight => ImGuiKey.ModSuper,
                Key.Menu => ImGuiKey.Menu,
                Key.Up => ImGuiKey.UpArrow,
                Key.Down => ImGuiKey.DownArrow,
                Key.Left => ImGuiKey.LeftArrow,
                Key.Right => ImGuiKey.RightArrow,
                Key.Enter => ImGuiKey.Enter,
                Key.Escape => ImGuiKey.Escape,
                Key.Space => ImGuiKey.Space,
                Key.Tab => ImGuiKey.Tab,
                Key.Backspace => ImGuiKey.Backspace,
                Key.Insert => ImGuiKey.Insert,
                Key.Delete => ImGuiKey.Delete,
                Key.PageUp => ImGuiKey.PageUp,
                Key.PageDown => ImGuiKey.PageDown,
                Key.Home => ImGuiKey.Home,
                Key.End => ImGuiKey.End,
                Key.CapsLock => ImGuiKey.CapsLock,
                Key.ScrollLock => ImGuiKey.ScrollLock,
                Key.PrintScreen => ImGuiKey.PrintScreen,
                Key.Pause => ImGuiKey.Pause,
                Key.NumLock => ImGuiKey.NumLock,
                Key.KeypadDivide => ImGuiKey.KeypadDivide,
                Key.KeypadMultiply => ImGuiKey.KeypadMultiply,
                Key.KeypadSubtract => ImGuiKey.KeypadSubtract,
                Key.KeypadAdd => ImGuiKey.KeypadAdd,
                Key.KeypadDecimal => ImGuiKey.KeypadDecimal,
                Key.KeypadEnter => ImGuiKey.KeypadEnter,
                Key.GraveAccent => ImGuiKey.GraveAccent,
                Key.Minus => ImGuiKey.Minus,
                Key.Equal => ImGuiKey.Equal,
                Key.LeftBracket => ImGuiKey.LeftBracket,
                Key.RightBracket => ImGuiKey.RightBracket,
                Key.Semicolon => ImGuiKey.Semicolon,
                Key.Apostrophe => ImGuiKey.Apostrophe,
                Key.Comma => ImGuiKey.Comma,
                Key.Period => ImGuiKey.Period,
                Key.Slash => ImGuiKey.Slash,
                Key.BackSlash => ImGuiKey.Backslash,
                _ => ImGuiKey.None
            };

            return result != ImGuiKey.None;
        }


        private void SetPerFrameImGuiData(float deltaSeconds)
        {
            var io = ImGui.GetIO();
            io.DisplaySize = new Vector2(_graphicsContext.MainSwapchain.Surface.Size.X, _graphicsContext.MainSwapchain.Surface.Size.Y);

            if (_graphicsContext.MainSwapchain.Surface.Size.X > 0 && _graphicsContext.MainSwapchain.Surface.Size.Y > 0)
            {
                io.DisplayFramebufferScale = new Vector2(1, 1);
            }
            io.DeltaTime = deltaSeconds; // DeltaTime is in seconds.
        }

        public void Update(WorldRenderer worldRenderer)
        {
            // Update monitors periodically (every 60 frames)
            if (_currentFrame % 60 == 0)
            {
                UpdateMonitors();
            }

            // Clean up texture cache every frame
            CleanupTextureCache();

            SetPerFrameImGuiData(Time.DeltaTime);
            UpdateImGuiInput();
            if ((ImGui.GetIO().ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0 && !_frameBegun)
            {
                ImGui.UpdatePlatformWindows();

            }

            if (!_frameBegun)
            {
                ImGui.NewFrame();
                _frameBegun = true;
            }

            // Only start new frame if not already begun

            _currentFrame++;
            foreach (var viewport in _viewportManager.Viewports)
            {
                if (!viewport.IsMainViewport)
                {
                    viewport.Window.ContinueEvents();
                    viewport.Window.DoEvents();
                    //viewport.Window.DoUpdate();
                    //viewport.Window.DoRender();
                }
            }
        }

        public void Render(UploadBatch batch, uint frameIndex, WorldRenderer renderer)
        {
            if (!_initialized)
            {
                return;
            }
            try
            {
                // Always ensure we have a valid frame before rendering
                if (!_frameBegun)
                {
                    ImGui.NewFrame();
                    _frameBegun = true;

                    // If we started the frame here, we need to end it immediately for this render
                    ImGui.Render();
                }
                else
                {
                    // If frame was begun in Update(), render it now
                    ImGui.Render();
                }

                RenderImDrawData(_viewportManager.MainViewport.ViewportPtr.DrawData, renderer.SwapchainTarget,batch, frameIndex, _viewportManager.MainViewport);
                if ((ImGui.GetIO().ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
                {

                    //ImGui.UpdatePlatformWindows();
                    var id = Guid.NewGuid();
                    var ptr = GCHandle.Alloc(new ViewportImguiStruct()
                    {
                        Batch = batch,
                        FrameIndex = frameIndex,
                    }, GCHandleType.Normal);
                    var nnt = GCHandle.ToIntPtr(ptr);
                    ImGui.RenderPlatformWindowsDefault(nnt, nnt);
                    ptr.Free();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message + Environment.NewLine + ex.StackTrace);
            }
            finally
            {
                // Reset frame state for next frame
                _frameBegun = false;
            }
            
        }
        internal struct ViewportImguiStruct
        {
            public UploadBatch Batch;
            public uint FrameIndex;
        }


        public unsafe void RenderImDrawData(ImDrawDataPtr drawData, SwapchainRenderTarget renderTarget, UploadBatch uploadBatch, uint frameIndex, RckImGuiViewport imguiViewport)
        {
            if (drawData.CmdListsCount == 0)
            {
                return;
            }
            renderTarget.PrepareForRender(uploadBatch);
            using (PerformanceTracer.BeginSection("IMGUI", uploadBatch, frameIndex))
            {
              
                unsafe
                {
                    fixed (ClearValue* pClearValue = renderTarget.ClearValues.Span)
                    {
                        var swapchainBeginInfo = new RenderPassBeginInfo
                        {
                            SType = StructureType.RenderPassBeginInfo,
                            RenderPass = _renderPass,
                            Framebuffer = renderTarget.Framebuffers[frameIndex],
                            RenderArea = new Rect2D { Extent = renderTarget.Size },
                            ClearValueCount = (uint)renderTarget.ClearValues.Length,
                            PClearValues = pClearValue
                        };

                        uploadBatch.BeginRenderPass(in swapchainBeginInfo, SubpassContents.Inline);
                    }
                }

                ref var buffers = ref GetOrCreateViewportBuffers(imguiViewport, frameIndex);

                // Ensure buffers exist and are large enough
                if (buffers.VertexBuffer == null || buffers.VertexBuffer.IsDisposed)
                {
                    buffers.VertexBuffer = VkBuffer.Create(_vkContext, 1024 * 1024, BufferUsageFlags.VertexBufferBit,
                        MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
                }

                if (buffers.IndexBuffer == null || buffers.IndexBuffer.IsDisposed)
                {
                    buffers.IndexBuffer = VkBuffer.Create(_vkContext, 512 * 1024, BufferUsageFlags.IndexBufferBit,
                        MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
                }

                ref var vertexBuffer = ref buffers.VertexBuffer;
                ref var indexBuffer = ref buffers.IndexBuffer;

                // Calculate required buffer sizes
                ulong requiredVertexSize = (ulong)(drawData.TotalVtxCount + 5000) * (ulong)Unsafe.SizeOf<ImDrawVert>();
                ulong requiredIndexSize = (ulong)(drawData.TotalIdxCount + 10000) * sizeof(ushort);

                // Ensure buffers are large enough
                if (vertexBuffer.Size < requiredVertexSize)
                {
                    vertexBuffer?.Dispose();
                    vertexBuffer = VkBuffer.Create(_vkContext, requiredVertexSize, BufferUsageFlags.VertexBufferBit,
                        MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
                }

                if (indexBuffer.Size < requiredIndexSize)
                {
                    indexBuffer?.Dispose();
                    indexBuffer = VkBuffer.Create(_vkContext, requiredIndexSize, BufferUsageFlags.IndexBufferBit,
                        MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
                }

                // Upload vertex/index data
                using (var pvtx_dst = vertexBuffer.MapMemory())
                {
                    using var pidx_dst = indexBuffer.MapMemory();
                    ImDrawVert* vtx_dst = (ImDrawVert*)pvtx_dst.Pointer;
                    ushort* idx_dst = (ushort*)pidx_dst.Pointer;

                    for (int n = 0; n < drawData.CmdListsCount; n++)
                    {
                        var cmd_list = drawData.CmdLists[n];
                        Unsafe.CopyBlock(vtx_dst, cmd_list.VtxBuffer.Data.ToPointer(), (uint)(cmd_list.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>()));
                        Unsafe.CopyBlock(idx_dst, cmd_list.IdxBuffer.Data.ToPointer(), (uint)(cmd_list.IdxBuffer.Size * sizeof(ushort)));
                        vtx_dst += cmd_list.VtxBuffer.Size;
                        idx_dst += cmd_list.IdxBuffer.Size;
                    }

                    pvtx_dst.Flush();
                    pidx_dst.Flush();
                }
               

              
               

                // Setup render state
                uploadBatch.BindPipeline(_pipeline);

                if (drawData.TotalVtxCount > 0)
                {
                    vertexBuffer.BindVertexBuffer(uploadBatch, 0);
                    indexBuffer.BindIndexBuffer(uploadBatch, 0, IndexType.Uint16);
                }

                // Setup viewport
                Viewport viewport = renderTarget.Viewport;
                uploadBatch.SetViewport(in viewport);

                // Setup scale and translation
                Span<float> scale = [2.0f / drawData.DisplaySize.X, 2.0f / drawData.DisplaySize.Y];
                Span<float> translate = [-1.0f - drawData.DisplayPos.X * scale[0], -1.0f - drawData.DisplayPos.Y * scale[1]];

                uploadBatch.PushConstants(_pipelineLayout, ShaderStageFlags.VertexBit, sizeof(float) * 0, sizeof(float) * 2, scale);
                uploadBatch.PushConstants(_pipelineLayout, ShaderStageFlags.VertexBit, sizeof(float) * 2, sizeof(float) * 2, translate);

                // Bind font texture
                _bindingManager.BindResource(frameIndex, _fontTextureBinding, uploadBatch, _pipelineLayout);

                // Render command lists
                Vector2 clipOff = drawData.DisplayPos;
                Vector2 clipScale = drawData.FramebufferScale;

                int global_vtx_offset = 0;
                int global_idx_offset = 0;


                for (int n = 0; n < drawData.CmdListsCount; n++)
                {
                    var cmd_list = drawData.CmdLists[n];
                    for (int cmd_i = 0; cmd_i < cmd_list.CmdBuffer.Size; cmd_i++)
                    {
                        ImDrawCmdPtr pcmd = cmd_list.CmdBuffer[cmd_i];
                        if (pcmd.UserCallback != IntPtr.Zero)
                        {
                            // Handle user callbacks if needed
                        }
                        else
                        {
                            if (pcmd.ElemCount == 0)
                            {
                                continue;
                            }

                            // Get texture binding
                            var textureBinding = GetTextureBindingFromId(pcmd.TextureId);
                            if (textureBinding != null)
                            {
                                _bindingManager.BindResource(frameIndex, textureBinding, uploadBatch, _pipelineLayout);
                            }

                            // Apply scissor/clipping rectangle
                            Vector4 clipRect;
                            clipRect.X = (pcmd.ClipRect.X - clipOff.X) * clipScale.X;
                            clipRect.Y = (pcmd.ClipRect.Y - clipOff.Y) * clipScale.Y;
                            clipRect.Z = (pcmd.ClipRect.Z - clipOff.X) * clipScale.X;
                            clipRect.W = (pcmd.ClipRect.W - clipOff.Y) * clipScale.Y;

                            if (clipRect.X < _graphicsContext.MainSwapchain.Extent.Width && clipRect.Y < _graphicsContext.MainSwapchain.Extent.Height && clipRect.Z >= 0.0f && clipRect.W >= 0.0f)
                            {
                                if (clipRect.X < 0.0f)
                                {
                                    clipRect.X = 0.0f;
                                }

                                if (clipRect.Y < 0.0f)
                                {
                                    clipRect.Y = 0.0f;
                                }

                                Rect2D scissor = new Rect2D
                                {
                                    Offset = { X = (int)clipRect.X, Y = (int)clipRect.Y },
                                    Extent = { Width = (uint)(clipRect.Z - clipRect.X), Height = (uint)(clipRect.W - clipRect.Y) }
                                };

                                uploadBatch.SetScissor(in scissor);
                                uploadBatch.DrawIndexed(pcmd.ElemCount, 1, pcmd.IdxOffset + (uint)global_idx_offset, (int)pcmd.VtxOffset + global_vtx_offset, 0);
                            }
                        }
                    }
                    global_idx_offset += cmd_list.IdxBuffer.Size;
                    global_vtx_offset += cmd_list.VtxBuffer.Size;
                }

                uploadBatch.EndRenderPass();
            }
        }

        private ref (VkBuffer VertexBuffer, VkBuffer IndexBuffer) GetOrCreateViewportBuffers(RckImGuiViewport viewport, uint frameIndex)
        {
            lock (_bufferLock)
            {
                if (!_viewportBuffers.TryGetValue(viewport, out var buffersArray))
                {
                    buffersArray = new (VkBuffer VertexBuffer, VkBuffer IndexBuffer)[_vkContext.MaxFramesPerFlight];
                    _viewportBuffers[viewport] = buffersArray;
                }

                return ref buffersArray[frameIndex];
            }
        }

        public void CleanupViewportBuffers(RckImGuiViewport viewport)
        {
            lock (_bufferLock)
            {
                if (_viewportBuffers.TryGetValue(viewport, out var buffersArray))
                {
                    for (int i = 0; i < buffersArray.Length; i++)
                    {
                        if(buffersArray[i].VertexBuffer is not null)
                        {
                            _vkContext.GraphicsSubmitContext.AddDependency(buffersArray[i].VertexBuffer);
                        }
                        if (buffersArray[i].IndexBuffer is not null)
                        {
                            _vkContext.GraphicsSubmitContext.AddDependency(buffersArray[i].IndexBuffer);
                        }
                    }
                    _viewportBuffers.Remove(viewport);
                }
            }
        }

        private void CreateOrResizeBuffer(ref VkBuffer? buffer, ulong size, BufferUsageFlags usage)
        {
            if (buffer is null || buffer.Size < size)
            {
                // Dispose of the old buffer if it exists
                buffer?.Dispose();

                // Calculate the new size with some growth factor to avoid frequent resizes
                ulong newSize = (ulong)(size * 1.2);  // 20% growth factor
                newSize = Math.Max(newSize, 1024 * 1024);  // Minimum size of 1 MB

                // Create the new buffer
                buffer = VkBuffer.Create(_vkContext, newSize, usage, MemoryPropertyFlags.HostVisibleBit);
            }
        }

        private unsafe void CreateDescriptorSet()
        {
            _fontTextureBinding = new TextureBinding(0, 0, 0, 1, ImageLayout.ShaderReadOnlyOptimal, _fontTexture);
            _bindingManager.AllocateAndUpdateDescriptorSet(0, _fontTextureBinding, _pipelineLayout);
        }

        private unsafe void CreateFontResources()
        {
            var io = ImGui.GetIO();
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

            // Use a list to track all GCHandles and ensure they stay alive
            List<GCHandle> fontHandles = new List<GCHandle>();

            try
            {
                // Load Roboto as main font
                byte[] robotoData = GetEmbeddedResourceBytes("RockEngine.Editor.Resources.Fonts.OpenSans-VariableFont_wdth,wght.ttf");
                GCHandle robotoHandle = GCHandle.Alloc(robotoData, GCHandleType.Pinned);
                fontHandles.Add(robotoHandle);

                // Main font - Roboto at 16px
                var fontConfig = new ImFontConfigPtr(ImGuiNative.ImFontConfig_ImFontConfig());
                fontConfig.FontDataOwnedByAtlas = false; // We manage the memory
                //fontConfig.FontNo = 0;

                io.Fonts.AddFontFromMemoryTTF(
                    robotoHandle.AddrOfPinnedObject(),
                    robotoData.Length,
                    16.0f,
                    fontConfig
                );

                // Don't destroy config yet - it might still be in use

                // Configure icon font merging
                var iconConfig = new ImFontConfigPtr(ImGuiNative.ImFontConfig_ImFontConfig());
                iconConfig.MergeMode = true;
                iconConfig.PixelSnapH = true;
                iconConfig.GlyphOffset = new Vector2(0, 2);
                iconConfig.FontDataOwnedByAtlas = false;
                //iconConfig.FontNo = 1;

                // Load Fork Awesome for icons
                byte[] iconFontData = GetEmbeddedResourceBytes("RockEngine.Editor.Resources.Fonts.forkawesome-webfont.ttf");
                GCHandle iconFontHandle = GCHandle.Alloc(iconFontData, GCHandleType.Pinned);
                fontHandles.Add(iconFontHandle);

                // Define icon ranges (Fork Awesome range: 0xf000-0xf2e0)
                ushort[] iconRanges = [0xf000, 0xf2e0, 0];
                fixed (ushort* rangesPtr = iconRanges)
                {
                    _iconFont = io.Fonts.AddFontFromMemoryTTF(
                        iconFontHandle.AddrOfPinnedObject(),
                        iconFontData.Length,
                        14.0f,
                        iconConfig,
                        (IntPtr)rangesPtr
                    );
                }

                // Build font atlas 
                io.Fonts.Build();

                // Now we can safely get texture data
                io.Fonts.GetTexDataAsRGBA32(out nint pixels, out int width, out int height, out int bytesPerPixel);

                // Create texture
                var bytes = new Span<byte>((void*)pixels, width * height * bytesPerPixel).ToArray();
                TextureData texData = new TextureData()
                {
                     Width = (uint)width,
                     Height = (uint)height,
                     Format = TextureFormat.R8G8B8A8Unorm,
                     GenerateMipmaps = false
                };
                //byte[] destinationArray = new byte[width * height * bytesPerPixel];
                //Marshal.Copy(pixels, destinationArray, 0, destinationArray.Length);
                _fontTexture =  Texture2D.CreateFromBytes(_vkContext, bytes: bytes, texData);

                // Store texture identifier
                io.Fonts.SetTexID(GetTextureID(_fontTexture));

                // Clear font data from RAM (GPU has the texture now)
                io.Fonts.ClearTexData();

                // Now destroy configs
                //fontConfig.Destroy();
                //iconConfig.Destroy();
            }
            finally
            {
                // Keep font data pinned until we're completely done
                // The GCHandles will be freed when the method ends and fontHandles goes out of scope
                // ImGui has copied the data it needs during Build()
            }
        }

        private unsafe ImFontPtr LoadFontFromResources(string resourcePath, float size, bool mergeMode = false, ushort[] glyphRanges = null)
        {
            var io = ImGui.GetIO();

            byte[] fontData = GetEmbeddedResourceBytes(resourcePath);
            GCHandle fontDataHandle = GCHandle.Alloc(fontData, GCHandleType.Pinned);

            try
            {
                if (mergeMode)
                {
                    var config = new ImFontConfigPtr(ImGuiNative.ImFontConfig_ImFontConfig());
                    config.MergeMode = true;
                    config.PixelSnapH = true;
                    config.GlyphOffset = new Vector2(0, 2);
                    config.FontDataOwnedByAtlas = false;

                    if (glyphRanges != null)
                    {
                        fixed (ushort* rangesPtr = glyphRanges)
                        {
                            return io.Fonts.AddFontFromMemoryTTF(
                                fontDataHandle.AddrOfPinnedObject(),
                                fontData.Length,
                                size,
                                config,
                                (IntPtr)rangesPtr
                            );
                        }
                    }
                    else
                    {

                        return io.Fonts.AddFontFromMemoryTTF(
                            fontDataHandle.AddrOfPinnedObject(),
                            fontData.Length,
                            size,
                            config
                        );
                    }
                }
                else
                {
                    return io.Fonts.AddFontFromMemoryTTF(
                        fontDataHandle.AddrOfPinnedObject(),
                        fontData.Length,
                        size
                    );
                }
            }
            finally
            {
                fontDataHandle.Free();
            }
        }

        private static byte[] GetEmbeddedResourceBytes(string resourcePath)
        {
            var assembly = Assembly.GetExecutingAssembly();

            // Debug output to verify resources
            var resourceNames = assembly.GetManifestResourceNames();
            if (!resourceNames.Contains(resourcePath))
            {
                throw new FileNotFoundException($"Resource '{resourcePath}' not found. Available resources: {string.Join(", ", resourceNames)}");
            }

            using Stream stream = assembly.GetManifestResourceStream(resourcePath) ?? throw new FileNotFoundException($"Resource '{resourcePath}' could not be opened.");
            byte[] data = new byte[stream.Length];
            int bytesRead = stream.Read(data, 0, data.Length);

            if (bytesRead != data.Length)
            {
                throw new InvalidDataException($"Failed to read complete resource '{resourcePath}'");
            }

            return data;
        }
        private TextureBinding GetTextureBindingFromId(IntPtr textureId)
        {
            if (textureId == IntPtr.Zero)
            {
                return null;
            }

            // The texture ID is the address of the Texture object
            // We need to find the corresponding TextureBinding
            var texture = GetTextureFromId(textureId);
            if (texture == null)
            {
                return null;
            }

            lock (_textureCacheLock)
            {
                if (_textureBindings.TryGetValue(texture, out var binding))
                {
                    return binding;
                }
            }
            return null;
        }

        // Helper method to get Texture from ID
        private unsafe Texture GetTextureFromId(IntPtr textureId)
        {
            // This assumes textureId is the address of the Texture object
            // You might need to adjust this based on how you're storing textures
            GCHandle handle = GCHandle.FromIntPtr(textureId);
            return handle.Target as Texture;
        }


        // Modify GetTextureID to return texture address instead of descriptor set handle
        public unsafe IntPtr GetTextureID(Texture texture)
        {
            if (texture == null || texture.IsDisposed)
            {
                return IntPtr.Zero;
            }

            lock (_textureCacheLock)
            {
                if (!_textureBindings.TryGetValue(texture, out _))
                {
                    // Create new texture binding
                    TextureBinding? binding = new TextureBinding(0, 0, 0, 1, ImageLayout.ShaderReadOnlyOptimal,texture);
                    _textureBindings[texture] = binding;

                    // Allocate descriptor sets for all frames
                    for (int i = 0; i < _vkContext.MaxFramesPerFlight; i++)
                    {
                        _bindingManager.AllocateDescriptorSet((uint)i, binding, _pipelineLayout);
                    }
                }

                // Return the address of the texture as the ID
                GCHandle handle = GCHandle.Alloc(texture, GCHandleType.Weak);
                return GCHandle.ToIntPtr(handle);
            }
        }

        // Modify CleanupTextureCache to handle texture bindings
        public void CleanupTextureCache()
        {
            lock (_textureCacheLock)
            {
                var texturesToRemove = new List<Texture>();

                foreach (var kvp in _textureBindings)
                {
                    if (kvp.Key.IsDisposed)
                    {
                        texturesToRemove.Add(kvp.Key);
                    }
                }

                foreach (var texture in texturesToRemove)
                {
                    _textureBindings.Remove(texture);
                }
            }
        }


        private void CreateDeviceObjects()
        {
            // Create shaders
            var vertShaderModule = VkShaderModule.Create(_vkContext, "Shaders/Imgui.vert.spv", ShaderStageFlags.VertexBit);
            var fragShaderModule = VkShaderModule.Create(_vkContext, "Shaders/Imgui.frag.spv", ShaderStageFlags.FragmentBit);


            SetPipeline(vertShaderModule, fragShaderModule);

        }
      

        private unsafe void SetPipeline(VkShaderModule vertShaderModule, VkShaderModule fragShaderModule)
        {
            _pipelineLayout = VkPipelineLayout.Create(_vkContext, vertShaderModule, fragShaderModule);
            _descriptorSetLayout = _pipelineLayout.DescriptorSetLayouts[0];

            var binding_desc = new VertexInputBindingDescription();
            binding_desc.Stride = (uint)Unsafe.SizeOf<ImDrawVert>();
            binding_desc.InputRate = VertexInputRate.Vertex;

            var color_attachment = new PipelineColorBlendAttachmentState();
            color_attachment.BlendEnable = new Silk.NET.Core.Bool32(true);
            color_attachment.SrcColorBlendFactor = BlendFactor.SrcAlpha;
            color_attachment.DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha;
            color_attachment.ColorBlendOp = BlendOp.Add;
            color_attachment.SrcAlphaBlendFactor = BlendFactor.One;
            color_attachment.DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha;
            color_attachment.AlphaBlendOp = BlendOp.Add;
            color_attachment.ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit;

            using GraphicsPipelineBuilder pipelineBuilder = new GraphicsPipelineBuilder(_vkContext, "Imgui")
                 .WithShaderModule(vertShaderModule)
                 .WithShaderModule(fragShaderModule)
                 .WithRasterizer(new VulkanRasterizerBuilder())
                 .WithInputAssembly(new VulkanInputAssemblyBuilder().Configure())
                 .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder()
                     .Add(binding_desc, [
                        new VertexInputAttributeDescription {
                            Location = 0, Binding = 0,
                            Format = Format.R32G32Sfloat,
                            Offset = (uint)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.pos))
                        },
                        new VertexInputAttributeDescription {
                            Location = 1, Binding = 0,
                            Format = Format.R32G32Sfloat,
                            Offset = (uint)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.uv))
                        },
                        new VertexInputAttributeDescription {
                            Location = 2, Binding = 0,
                            Format = Format.R8G8B8A8Unorm,
                            Offset = (uint)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.col))
                        }
                         ]))
                 .WithViewportState(new VulkanViewportStateInfoBuilder()
                     .AddViewport(new Viewport() { Height = _graphicsContext.MainSwapchain.Surface.Size.X, Width = _graphicsContext.MainSwapchain.Surface.Size.Y })
                     .AddScissors(new Rect2D()))
                 .WithMultisampleState(new VulkanMultisampleStateInfoBuilder().Configure(false, SampleCountFlags.Count1Bit))
                 .WithColorBlendState(new VulkanColorBlendStateBuilder()
                     .AddAttachment(color_attachment))
                 .AddRenderPass(_renderPass)
                 .WithSubpass<ImGuiPass>()
                 .WithPipelineLayout(_pipelineLayout)
                 .WithDynamicState(new PipelineDynamicStateBuilder()
                    .AddState(DynamicState.Viewport)
                    .AddState(DynamicState.Scissor)
                    );

            _pipeline = pipelineBuilder.Build();
        }

        public void Dispose()
        {
            _initialized = false;

        }


    }
}