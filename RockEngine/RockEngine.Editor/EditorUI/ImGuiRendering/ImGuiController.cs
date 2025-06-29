using ImGuiNET;

using RockEngine.Core.Extensions.Builders;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;
using RockEngine.Vulkan.Builders;

using Silk.NET.Input;
using Silk.NET.Vulkan;

using SkiaSharp;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.ImGuiRendering
{
    public class ImGuiController : IDisposable
    {
        private const string ImguiRenderPass = "ImGuiPass";
        private readonly VulkanContext _vkContext;
        private readonly GraphicsEngine _graphicsEngine;
        private readonly IInputContext _input;
        private VkDescriptorPool _descriptorPool;
        private VkPipelineLayout _pipelineLayout;
        private VkDescriptorSetLayout _descriptorSetLayout;
        private readonly VkBuffer?[] _vertexBuffers;
        private readonly VkBuffer?[] _indexBuffers;
        private bool _frameBegun;
        private VkRenderPass _renderPass;
        private readonly BindingManager _bindingManager;
        private VkPipeline _pipeline;
        private readonly Queue<char> _pressedChars = new Queue<char>();
        private readonly ulong _bufferMemoryAlignment;
        private Texture _fontTexture;
        private VkDescriptorSet _fontTextureDescriptorSet;
        private Dictionary<Texture, (GCHandle, TextureBinding)> _textures = new Dictionary<Texture, (GCHandle, TextureBinding)>();
        private RenderTarget _uiRenderTarget;
        private VkFrameBuffer[] _framebuffers;

        public unsafe ImGuiController(VulkanContext vkContext, GraphicsEngine graphicsEngine, BindingManager bindingManager, IInputContext inputContext, uint width, uint height, RenderTarget renderTarget)
        {
            _vkContext = vkContext;
            _bindingManager = bindingManager;
            _graphicsEngine = graphicsEngine;
            _input = inputContext;
            _uiRenderTarget = renderTarget;
            _renderPass = _uiRenderTarget.RenderPass;
            ImGui.CreateContext();
            var io = ImGui.GetIO();
            io.Fonts.AddFontDefault();
            _vertexBuffers = new VkBuffer[_vkContext.MaxFramesPerFlight];
            _indexBuffers = new VkBuffer[_vkContext.MaxFramesPerFlight];
            CreateDescriptorPool();
            CreateDeviceObjects();
            CreateFontResources();
            CreateDescriptorSet();
            ApplyModernDarkTheme();
            ImGui.GetIO().DisplaySize = new Vector2(width, height);
            _input.Keyboards[0].KeyChar += (s, c) => PressChar(c);



            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;
            io.BackendFlags |= ImGuiBackendFlags.HasSetMousePos;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            SetPerFrameImGuiData(1f / 60f);
            ImGui.NewFrame();

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
            if (_frameBegun)
            {
                ImGui.Render();
            }

            SetPerFrameImGuiData(Time.DeltaTime);
            UpdateImGuiInput(_input);
            _frameBegun = true;
            ImGui.EndFrame();

            ImGui.NewFrame();
        }

        public unsafe void Render(VkCommandBuffer commandBuffer, Extent2D swapChainExtent)
        {
            if (!_frameBegun)
            {
                return;
            }

            _frameBegun = false;
            ImGui.Render();
            RenderImDrawData(ImGui.GetDrawData(), commandBuffer, swapChainExtent);
        }

        private unsafe void RenderImDrawData(ImDrawDataPtr drawData, VkCommandBuffer commandBuffer, Extent2D swapChainExtent)
        {

            ref var vertexBuffer = ref _vertexBuffers[_graphicsEngine.CurrentImageIndex];
            ref var indexBuffer = ref _indexBuffers![_graphicsEngine.CurrentImageIndex];
            if (drawData.TotalVtxCount > 0)
            {
                // Calculate required buffer sizes
                ulong requiredVertexSize = (ulong)drawData.TotalVtxCount * (ulong)Unsafe.SizeOf<ImDrawVert>();
                ulong requiredIndexSize = (ulong)drawData.TotalIdxCount * sizeof(ushort);

                // Create or resize the vertex/index buffers
                CreateOrResizeBuffer(ref vertexBuffer, requiredVertexSize, BufferUsageFlags.VertexBufferBit);
                CreateOrResizeBuffer(ref indexBuffer, requiredIndexSize, BufferUsageFlags.IndexBufferBit);
                // Upload vertex/index data into a single contiguous GPU buffer
                vertexBuffer!.Map(out var pvtx_dst);
                indexBuffer!.Map(out var pidx_dst);
                ImDrawVert* vtx_dst = (ImDrawVert*)pvtx_dst;
                ushort* idx_dst = (ushort*)pidx_dst;

                for (int n = 0; n < drawData.CmdListsCount; n++)
                {
                    var cmd_list = drawData.CmdLists[n];
                    Unsafe.CopyBlock(vtx_dst, cmd_list.VtxBuffer.Data.ToPointer(), (uint)cmd_list.VtxBuffer.Size * (uint)sizeof(ImDrawVert));
                    Unsafe.CopyBlock(idx_dst, cmd_list.IdxBuffer.Data.ToPointer(), (uint)cmd_list.IdxBuffer.Size * sizeof(ushort));
                    vtx_dst += cmd_list.VtxBuffer.Size;
                    idx_dst += cmd_list.IdxBuffer.Size;
                }
                vertexBuffer.Flush();
                indexBuffer.Flush();
                vertexBuffer.Unmap();
                indexBuffer.Unmap();
            }

            // Setup desired Vulkan state
            VulkanContext.Vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _pipeline);
            // Bind Vertex And Index Buffer:
            if (drawData.TotalVtxCount > 0)
            {
                ulong vertex_offset = 0;
                vertexBuffer!.BindVertexBuffer(commandBuffer, vertex_offset);
                indexBuffer!.BindIndexBuffer(commandBuffer, 0, sizeof(ushort) == 2 ? IndexType.Uint16 : IndexType.Uint32);
            }

            // Setup viewport:
            Viewport viewport;
            viewport.X = 0;
            viewport.Y = 0;
            viewport.Width = _graphicsEngine.Swapchain.Extent.Width;
            viewport.Height = _graphicsEngine.Swapchain.Extent.Height;
            viewport.MinDepth = 0.0f;
            viewport.MaxDepth = 1.0f;
            commandBuffer.SetViewport(in viewport);

            // Setup scale and translation:
            // Our visible imgui space lies from draw_data.DisplayPps (top left) to draw_data.DisplayPos+data_data.DisplaySize (bottom right). DisplayPos is (0,0) for single viewport apps.
            Span<float> scale = stackalloc float[2];
            scale[0] = 2.0f / drawData.DisplaySize.X;
            scale[1] = 2.0f / drawData.DisplaySize.Y;
            Span<float> translate = stackalloc float[2];
            translate[0] = -1.0f - drawData.DisplayPos.X * scale[0];
            translate[1] = -1.0f - drawData.DisplayPos.Y * scale[1];
            VulkanContext.Vk.CmdPushConstants(commandBuffer, _pipelineLayout, ShaderStageFlags.VertexBit, sizeof(float) * 0, sizeof(float) * 2, scale);
            VulkanContext.Vk.CmdPushConstants(commandBuffer, _pipelineLayout, ShaderStageFlags.VertexBit, sizeof(float) * 2, sizeof(float) * 2, translate);

            var fontSet = _fontTextureDescriptorSet.VkObjectNative;
            VulkanContext.Vk.CmdBindDescriptorSets(
                commandBuffer,
                PipelineBindPoint.Graphics,
                _pipelineLayout,
                0, // First set
                1, // Descriptor set count
                in fontSet, // Pointer to descriptor set
                0,
                null
            );
            // Will project scissor/clipping rectangles into framebuffer space
            Vector2 clipOff = drawData.DisplayPos;         // (0,0) unless using multi-viewports
            Vector2 clipScale = drawData.FramebufferScale; // (1,1) unless using retina display which are often (2,2)

            // Render command lists
            // (Because we merged all buffers into a single one, we maintain our own offset into them)
            int vertexOffset = 0;
            int indexOffset = 0;
            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                var cmd_list = drawData.CmdLists[n];
                for (int cmd_i = 0; cmd_i < cmd_list.CmdBuffer.Size; cmd_i++)
                {
                    ImDrawCmdPtr pcmd = cmd_list.CmdBuffer[cmd_i];
                    if (pcmd.ElemCount == 0) continue;

                    // Get the descriptor set from the pinned handle
                    VkDescriptorSet descriptor = default;
                    var handle = GCHandle.FromIntPtr(pcmd.TextureId);
                    descriptor = (VkDescriptorSet)handle.Target;
                    if (descriptor is null)
                    {
                        continue;
                    }
                    var descriptorHandle = descriptor.VkObjectNative;

                    VulkanContext.Vk.CmdBindDescriptorSets(
                        commandBuffer,
                        PipelineBindPoint.Graphics,
                        _pipelineLayout,
                        0, // First set
                        1, // Descriptor set count
                        in descriptorHandle, // Pointer to descriptor set
                        0,
                        null
                    );


                    // Project scissor/clipping rectangles into framebuffer space
                    Vector4 clipRect;
                    clipRect.X = (pcmd.ClipRect.X - clipOff.X) * clipScale.X;
                    clipRect.Y = (pcmd.ClipRect.Y - clipOff.Y) * clipScale.Y;
                    clipRect.Z = (pcmd.ClipRect.Z - clipOff.X) * clipScale.X;
                    clipRect.W = (pcmd.ClipRect.W - clipOff.Y) * clipScale.Y;

                    if (clipRect.X < _graphicsEngine.Swapchain.Extent.Width && clipRect.Y < _graphicsEngine.Swapchain.Extent.Height && clipRect.Z >= 0.0f && clipRect.W >= 0.0f)
                    {
                        // Negative offsets are illegal for vkCmdSetScissor
                        if (clipRect.X < 0.0f)
                            clipRect.X = 0.0f;
                        if (clipRect.Y < 0.0f)
                            clipRect.Y = 0.0f;

                        // Apply scissor/clipping rectangle
                        Rect2D scissor = new Rect2D();
                        scissor.Offset.X = (int)clipRect.X;
                        scissor.Offset.Y = (int)clipRect.Y;
                        scissor.Extent.Width = (uint)(clipRect.Z - clipRect.X);
                        scissor.Extent.Height = (uint)(clipRect.W - clipRect.Y);
                        VulkanContext.Vk.CmdSetScissor(commandBuffer, 0, 1, &scissor);

                        // Draw
                        VulkanContext.Vk.CmdDrawIndexed(commandBuffer, pcmd.ElemCount, 1, pcmd.IdxOffset + (uint)indexOffset, (int)pcmd.VtxOffset + vertexOffset, 0);
                    }
                }
                indexOffset += cmd_list.IdxBuffer.Size;
                vertexOffset += cmd_list.VtxBuffer.Size;
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

        private unsafe void CreateDescriptorPool()
        {
            var poolSizes = new[]
            {
                new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = 1000 }
            };

            var poolInfo = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                PoolSizeCount = (uint)poolSizes.Length,
                PPoolSizes = (DescriptorPoolSize*)Unsafe.AsPointer(ref poolSizes[0]),
                MaxSets = 100,
                Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
            };

            _descriptorPool = VkDescriptorPool.Create(_vkContext, in poolInfo);
        }


        private unsafe void CreateDescriptorSet()
        {
            var layout = _descriptorSetLayout.DescriptorSetLayout;
            var set = _descriptorPool.AllocateDescriptorSet(layout);
            _fontTextureDescriptorSet = set;
            var imageInfo = new DescriptorImageInfo
            {
                Sampler = _fontTexture.Sampler,
                ImageView = _fontTexture.ImageView,
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal
            };

            var descriptorWrite = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _fontTextureDescriptorSet,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.CombinedImageSampler,
                PImageInfo = &imageInfo
            };

            VulkanContext.Vk.UpdateDescriptorSets(_vkContext.Device, 1, &descriptorWrite, 0, null);
        }

        private unsafe void CreateFontResources()
        {

            var io = ImGui.GetIO();
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            io.Fonts.GetTexDataAsRGBA32(out nint pixels, out int width, out int height, out int bytes_per_pixel);
            Span<byte> bytes = new Span<byte>((void*)pixels, width * height * bytes_per_pixel);
            _fontTexture = Texture.Create(_vkContext, width, height, Format.R8G8B8A8Unorm, bytes);

            // Store our identifier
            io.Fonts.SetTexID(GetTextureID(_fontTexture));
        }

        public unsafe nint GetTextureID(Texture texture)
        {
            if (_textures.TryGetValue(texture, out var value))
            {
                var desc = value.Item2.DescriptorSets[_graphicsEngine.CurrentImageIndex];
                if (desc is null || desc.IsDirty)
                {
                    value.Item1.Free();
                    _bindingManager.AllocateAndUpdateDescriptorSet(_graphicsEngine.CurrentImageIndex, value.Item2, _pipelineLayout);
                    value.Item2.DescriptorSets[_graphicsEngine.CurrentImageIndex].IsDirty = false;
                    var newHandle = GCHandle.Alloc(value.Item2.DescriptorSets[_graphicsEngine.CurrentImageIndex]);
                    _textures[texture] = (newHandle, value.Item2);
                    return GCHandle.ToIntPtr(newHandle);
                }
                return GCHandle.ToIntPtr(value.Item1);
            }
            var binding = new TextureBinding(0, 0, ImageLayout.ShaderReadOnlyOptimal, texture);
            _bindingManager.AllocateAndUpdateDescriptorSet(_graphicsEngine.CurrentImageIndex, binding, _pipelineLayout);
            var handle = GCHandle.Alloc(binding.DescriptorSets[_graphicsEngine.CurrentImageIndex]);
            _textures[texture] = (handle, binding);
            return GCHandle.ToIntPtr(handle);
        }

        private void CreateDeviceObjects()
        {
            // Create shaders
            var vertShaderModule = VkShaderModule.Create(_vkContext, "Shaders/Imgui.vert.spv", ShaderStageFlags.VertexBit);
            var fragShaderModule = VkShaderModule.Create(_vkContext, "Shaders/Imgui.frag.spv", ShaderStageFlags.FragmentBit);


            SetPipeline(vertShaderModule, fragShaderModule);

        }
        private void CreateFramebuffers()
        {
            _framebuffers = new VkFrameBuffer[1]; // Single framebuffer for UI
            var attachments = new[] { _uiRenderTarget.OutputTexture.ImageView };
            _framebuffers[0] = VkFrameBuffer.Create(
                _vkContext,
                _renderPass,
                attachments,
                _uiRenderTarget.Size.Width,
                _uiRenderTarget.Size.Height
            );
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
                 .WithPipelineLayout(_pipelineLayout)
                 .WithDynamicState(new PipelineDynamicStateBuilder()
                    .AddState(DynamicState.Viewport)
                    .AddState(DynamicState.Scissor)
                    );

            _pipeline = pipelineBuilder.Build();
        }


        public static void ApplyModernDarkTheme()
        {
            var style = ImGui.GetStyle();
            var colors = style.Colors;

            // Increased spacing and rounded corners
            style.WindowPadding = new Vector2(12, 12);
            style.WindowRounding = 8.0f;
            style.FramePadding = new Vector2(8, 4);
            style.FrameRounding = 6.0f;
            style.PopupRounding = 4.0f;
            style.ChildRounding = 8.0f;
            style.ScrollbarRounding = 6.0f;
            style.GrabRounding = 4.0f;
            style.TabRounding = 6.0f;
            style.ItemSpacing = new Vector2(10, 8);
            style.ItemInnerSpacing = new Vector2(6, 4);
            style.IndentSpacing = 20.0f;
            style.ScrollbarSize = 12.0f;

            // Modern dark color palette
            var bgColor = new Vector4(0.08f, 0.08f, 0.08f, 1.00f);
            var darkColor = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);
            var accentColor = new Vector4(0.16f, 0.44f, 0.75f, 1.00f);
            var accentHoverColor = new Vector4(0.20f, 0.54f, 0.85f, 1.00f);
            var textColor = new Vector4(0.92f, 0.92f, 0.92f, 1.00f);

            colors[(int)ImGuiCol.Text] = textColor;
            colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.50f, 0.50f, 0.50f, 1.00f);
            colors[(int)ImGuiCol.WindowBg] = bgColor;
            colors[(int)ImGuiCol.ChildBg] = darkColor;
            colors[(int)ImGuiCol.PopupBg] = darkColor;
            colors[(int)ImGuiCol.Border] = new Vector4(0.18f, 0.18f, 0.18f, 0.50f);
            colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);

            // Interactive elements
            colors[(int)ImGuiCol.FrameBg] = darkColor;
            colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.20f, 0.20f, 0.20f, 1.00f);
            colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.22f, 0.22f, 0.22f, 1.00f);

            // Buttons
            colors[(int)ImGuiCol.Button] = new Vector4(0.20f, 0.20f, 0.20f, 1.00f);
            colors[(int)ImGuiCol.ButtonHovered] = accentColor;
            colors[(int)ImGuiCol.ButtonActive] = accentHoverColor;

            // Headers
            colors[(int)ImGuiCol.Header] = accentColor;
            colors[(int)ImGuiCol.HeaderHovered] = accentHoverColor;
            colors[(int)ImGuiCol.HeaderActive] = accentHoverColor;

            // Titles
            colors[(int)ImGuiCol.TitleBg] = darkColor;
            colors[(int)ImGuiCol.TitleBgActive] = darkColor;
            colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.00f, 0.00f, 0.00f, 0.51f);

            // Scrollbars
            colors[(int)ImGuiCol.ScrollbarBg] = darkColor;
            colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.35f, 0.35f, 0.35f, 1.00f);
            colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.40f, 0.40f, 0.40f, 1.00f);
            colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.45f, 0.45f, 0.45f, 1.00f);

            // Sliders
            colors[(int)ImGuiCol.SliderGrab] = accentColor;
            colors[(int)ImGuiCol.SliderGrabActive] = accentHoverColor;

            // Check marks
            colors[(int)ImGuiCol.CheckMark] = accentColor;

            // Tabs
            colors[(int)ImGuiCol.Tab] = darkColor;
            colors[(int)ImGuiCol.TabHovered] = accentColor;
            /* colors[(int)ImGuiCol.TabActive] = accentHoverColor;
             colors[(int)ImGuiCol.TabUnfocused] = darkColor;
             colors[(int)ImGuiCol.TabUnfocusedActive] = darkColor;*/

            // Docking
            colors[(int)ImGuiCol.DockingPreview] = accentColor * new Vector4(1.0f, 1.0f, 1.0f, 0.7f);
            colors[(int)ImGuiCol.DockingEmptyBg] = new Vector4(0.10f, 0.10f, 0.10f, 1.00f);

            // Separators
            colors[(int)ImGuiCol.Separator] = new Vector4(0.20f, 0.20f, 0.20f, 1.00f);
            colors[(int)ImGuiCol.SeparatorHovered] = accentColor;
            colors[(int)ImGuiCol.SeparatorActive] = accentHoverColor;

            // Resize grips
            colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.30f, 0.30f, 0.30f, 0.20f);
            colors[(int)ImGuiCol.ResizeGripHovered] = accentColor;
            colors[(int)ImGuiCol.ResizeGripActive] = accentHoverColor;

            // Plot lines
            colors[(int)ImGuiCol.PlotLines] = accentColor;
            colors[(int)ImGuiCol.PlotLinesHovered] = accentHoverColor;
            colors[(int)ImGuiCol.PlotHistogram] = accentColor;
            colors[(int)ImGuiCol.PlotHistogramHovered] = accentHoverColor;

            // Text selection
            colors[(int)ImGuiCol.TextSelectedBg] = accentColor * new Vector4(0.24f, 0.45f, 0.68f, 0.35f);
        }

        public void Dispose()
        {

        }


    }
}