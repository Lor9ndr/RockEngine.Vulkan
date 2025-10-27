using ImGuiNET;

using RockEngine.Core;
using RockEngine.Core.Builders;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Editor.EditorUI.EditorWindows;
using RockEngine.Editor.SubPasses;
using RockEngine.Vulkan;

using Silk.NET.Input;
using Silk.NET.Vulkan;

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
        private readonly GraphicsEngine _graphicsEngine;
        private readonly IInputContext _input;
        private VkPipelineLayout _pipelineLayout;
        private VkDescriptorSetLayout _descriptorSetLayout;
        private readonly VkBuffer?[] _vertexBuffers;
        private readonly VkBuffer?[] _indexBuffers;
        private bool _frameBegun;
        private RckRenderPass _renderPass;
        private readonly BindingManager _bindingManager;
        private VkPipeline _pipeline;
        private readonly Queue<char> _pressedChars = new Queue<char>();
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
        private bool _texturesInitialized = false;

        public ImFontPtr IconFont { get => _iconFont; set => _iconFont = value; }

        public unsafe ImGuiController(VulkanContext vkContext, GraphicsEngine graphicsEngine, BindingManager bindingManager, IInputContext inputContext, Renderer renderer)
        {
            _vkContext = vkContext;
            _bindingManager = bindingManager;
            _graphicsEngine = graphicsEngine;
            _input = inputContext;
            _uiRenderTarget = renderer.SwapchainTarget;
            _renderPass = _uiRenderTarget.RenderPass;
            
            ImGui.SetCurrentContext(ImGui.CreateContext());
            var io = ImGui.GetIO();
            _vertexBuffers = new VkBuffer[_vkContext.MaxFramesPerFlight];
            _indexBuffers = new VkBuffer[_vkContext.MaxFramesPerFlight];
           
            _input.Keyboards[0].KeyChar += (s, c) => PressChar(c);

            for (int i = 0; i < _vkContext.MaxFramesPerFlight; i++)
            {
                _vertexBuffers[i] = VkBuffer.Create(_vkContext, 1024 * 1024, BufferUsageFlags.VertexBufferBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
                _indexBuffers[i] = VkBuffer.Create(_vkContext, 512 * 1024, BufferUsageFlags.IndexBufferBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            }


            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;
            io.BackendFlags |= ImGuiBackendFlags.HasSetMousePos;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            SetPerFrameImGuiData(1f / 60f);
            Init();

        }
        private void Init()
        {
            if (_initialized)
            {
                return;
            }
            CreateDeviceObjects();
            CreateFontResources();
            CreateDescriptorSet();
            EditorTheme.ApplyModernDarkTheme();
            ImGui.NewFrame();
            _frameBegun = true;
            _initialized = true;
        }
        private void UpdateImGuiInput(IInputContext input)
        {
            var io = ImGui.GetIO();

            var mouseState = input.Mice[0];
            var keyboardState = input.Keyboards[0];

            io.MouseDown[0] = mouseState.IsButtonPressed(MouseButton.Left);
            io.MouseDown[1] = mouseState.IsButtonPressed(MouseButton.Right);
            io.MouseDown[2] = mouseState.IsButtonPressed(MouseButton.Middle);

            io.MousePos = mouseState.Position;

            var wheel = mouseState.ScrollWheels[0];
            io.MouseWheel = wheel.Y;
            io.MouseWheelH = wheel.X;

            foreach (Key key in keyboardState.SupportedKeys)
            {
                if (key == Key.Unknown)
                {
                    continue;
                }
                if (TryMapKey(key, out ImGuiKey imguikey))
                {
                    io.AddKeyEvent(imguikey, keyboardState.IsKeyPressed(key));
                }
            }

            while (_pressedChars.Count > 0)
            {
                io.AddInputCharacter(_pressedChars.Dequeue());
            }


            io.KeyCtrl = keyboardState.IsKeyPressed(Key.ControlLeft) || keyboardState.IsKeyPressed(Key.ControlRight);
            io.KeyAlt = keyboardState.IsKeyPressed(Key.AltLeft) || keyboardState.IsKeyPressed(Key.AltRight);
            io.KeyShift = keyboardState.IsKeyPressed(Key.ShiftLeft) || keyboardState.IsKeyPressed(Key.ShiftRight);
            io.KeySuper = keyboardState.IsKeyPressed(Key.SuperLeft) || keyboardState.IsKeyPressed(Key.SuperRight);
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

        internal void PressChar(char keyChar)
        {
            _pressedChars.Enqueue(keyChar);
        }

        private void SetPerFrameImGuiData(float deltaSeconds)
        {
            var io = ImGui.GetIO();
            io.DisplaySize = new Vector2(_graphicsEngine.Swapchain.Surface.Size.X, _graphicsEngine.Swapchain.Surface.Size.Y);

            if (_graphicsEngine.Swapchain.Surface.Size.X > 0 && _graphicsEngine.Swapchain.Surface.Size.Y > 0)
            {
                io.DisplayFramebufferScale = new Vector2(1, 1);
            }
            io.DeltaTime = deltaSeconds; // DeltaTime is in seconds.
        }


        public void Update()
        {
            // Clean up texture cache every frame
            CleanupTextureCache();

            if (_frameBegun)
            {
                ImGui.Render();
                _frameBegun = false;
            }

            SetPerFrameImGuiData(Time.DeltaTime);
            UpdateImGuiInput(_input);
            ImGui.NewFrame();
            _frameBegun = true;
            _currentFrame++;
        }

        public unsafe void Render(VkCommandBuffer commandBuffer, uint frameIndex)
        {
            if (!_frameBegun || !_initialized)
            {
                return;
            }

            // Ensure we're using a valid frame index
            frameIndex %= (uint)_vkContext.MaxFramesPerFlight;

            ImGui.Render();
            RenderImDrawData(ImGui.GetDrawData(), commandBuffer, frameIndex);
        }


         private unsafe void RenderImDrawData(ImDrawDataPtr drawData, VkCommandBuffer commandBuffer, uint frameIndex)
        {
            if (drawData.CmdListsCount == 0)
            {
                return;
            }

            ref var vertexBuffer = ref _vertexBuffers[frameIndex];
            ref var indexBuffer = ref _indexBuffers[frameIndex];
            
            // Calculate required buffer sizes with some padding
            ulong requiredVertexSize = (ulong)(drawData.TotalVtxCount + 5000) * (ulong)Unsafe.SizeOf<ImDrawVert>();
            ulong requiredIndexSize = (ulong)(drawData.TotalIdxCount + 10000) * sizeof(ushort);

            // Ensure buffers are large enough
            if (vertexBuffer.Size < requiredVertexSize)
            {
                vertexBuffer?.Dispose();
                vertexBuffer = VkBuffer.Create(_vkContext, requiredVertexSize, BufferUsageFlags.VertexBufferBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            }

            if (indexBuffer.Size < requiredIndexSize)
            {
                indexBuffer?.Dispose();
                indexBuffer = VkBuffer.Create(_vkContext, requiredIndexSize, BufferUsageFlags.IndexBufferBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            }

            // Upload vertex/index data
            vertexBuffer.Map(out var pvtx_dst);
            indexBuffer.Map(out var pidx_dst);
            
            ImDrawVert* vtx_dst = (ImDrawVert*)pvtx_dst;
            ushort* idx_dst = (ushort*)pidx_dst;

            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                var cmd_list = drawData.CmdListsRange[n];
                Unsafe.CopyBlock(vtx_dst, cmd_list.VtxBuffer.Data.ToPointer(), (uint)(cmd_list.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>()));
                Unsafe.CopyBlock(idx_dst, cmd_list.IdxBuffer.Data.ToPointer(), (uint)(cmd_list.IdxBuffer.Size * sizeof(ushort)));
                vtx_dst += cmd_list.VtxBuffer.Size;
                idx_dst += cmd_list.IdxBuffer.Size;
            }
            
            vertexBuffer.Flush();
            indexBuffer.Flush();
            vertexBuffer.Unmap();
            indexBuffer.Unmap();

            // Setup render state
            VulkanContext.Vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _pipeline);
            
            if (drawData.TotalVtxCount > 0)
            {
                vertexBuffer.BindVertexBuffer(commandBuffer, 0);
                indexBuffer.BindIndexBuffer(commandBuffer, 0, IndexType.Uint16);
            }

            // Setup viewport
            Viewport viewport;
            viewport.X = 0;
            viewport.Y = 0;
            viewport.Width = _graphicsEngine.Swapchain.Extent.Width;
            viewport.Height = _graphicsEngine.Swapchain.Extent.Height;
            viewport.MinDepth = 0.0f;
            viewport.MaxDepth = 1.0f;
            commandBuffer.SetViewport(in viewport);

            // Setup scale and translation
            Span<float> scale = [2.0f / drawData.DisplaySize.X, 2.0f / drawData.DisplaySize.Y];
            Span<float> translate = [-1.0f - drawData.DisplayPos.X * scale[0], -1.0f - drawData.DisplayPos.Y * scale[1]];

            VulkanContext.Vk.CmdPushConstants(commandBuffer, _pipelineLayout, ShaderStageFlags.VertexBit, sizeof(float) * 0, sizeof(float) * 2, scale);
            VulkanContext.Vk.CmdPushConstants(commandBuffer, _pipelineLayout, ShaderStageFlags.VertexBit, sizeof(float) * 2, sizeof(float) * 2, translate);

            // Bind font texture
            _bindingManager.BindResource(frameIndex, _fontTextureBinding, commandBuffer, _pipelineLayout);

            // Render command lists
            Vector2 clipOff = drawData.DisplayPos;
            Vector2 clipScale = drawData.FramebufferScale;
            
            int global_vtx_offset = 0;
            int global_idx_offset = 0;
            
            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                var cmd_list = drawData.CmdListsRange[n];
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
                            _bindingManager.BindResource(frameIndex, textureBinding, commandBuffer, _pipelineLayout);
                        }

                        // Apply scissor/clipping rectangle
                        Vector4 clipRect;
                        clipRect.X = (pcmd.ClipRect.X - clipOff.X) * clipScale.X;
                        clipRect.Y = (pcmd.ClipRect.Y - clipOff.Y) * clipScale.Y;
                        clipRect.Z = (pcmd.ClipRect.Z - clipOff.X) * clipScale.X;
                        clipRect.W = (pcmd.ClipRect.W - clipOff.Y) * clipScale.Y;

                        if (clipRect.X < _graphicsEngine.Swapchain.Extent.Width && clipRect.Y < _graphicsEngine.Swapchain.Extent.Height && clipRect.Z >= 0.0f && clipRect.W >= 0.0f)
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
                            
                            VulkanContext.Vk.CmdSetScissor(commandBuffer, 0, 1, &scissor);
                            VulkanContext.Vk.CmdDrawIndexed(commandBuffer, pcmd.ElemCount, 1, pcmd.IdxOffset + (uint)global_idx_offset, (int)pcmd.VtxOffset + global_vtx_offset, 0);
                        }
                    }
                }
                global_idx_offset += cmd_list.IdxBuffer.Size;
                global_vtx_offset += cmd_list.VtxBuffer.Size;
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
            _fontTextureBinding = new TextureBinding(0, 0, 0, 1, _fontTexture);
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

                // Build font atlas - this is where the crash occurs
                io.Fonts.Build();

                // Now we can safely get texture data
                io.Fonts.GetTexDataAsRGBA32(out nint pixels, out int width, out int height, out int bytesPerPixel);

                // Create texture
                Span<byte> bytes = new Span<byte>((void*)pixels, width * height * bytesPerPixel);
                _fontTexture = Texture2D.Create(_vkContext, width, height, Format.R8G8B8A8Unorm, bytes);

                // Store texture identifier
                io.Fonts.SetTexID(GetTextureID(_fontTexture));

                // Clear font data from RAM (GPU has the texture now)
                io.Fonts.ClearTexData();

                // Now destroy configs
                fontConfig.Destroy();
                iconConfig.Destroy();
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
                if (!_textureBindings.TryGetValue(texture, out var binding))
                {
                    // Create new texture binding
                    binding = new TextureBinding(0, 0, 0, 1, texture);
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
                     .AddViewport(new Viewport() { Height = _graphicsEngine.Swapchain.Surface.Size.X, Width = _graphicsEngine.Swapchain.Surface.Size.Y })
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