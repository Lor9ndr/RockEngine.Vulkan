using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;
using RockEngine.Vulkan;
using System;
using Silk.NET.Core.Native;
using System.Runtime.InteropServices;
using Silk.NET.Input;
using ImGuiNET;

namespace RockEngine.Core.Rendering.ImGuiRendering
{
    public class ImGuiController : IDisposable
    {
        private const string ImguiRenderPass = "ImGuiPass";
        private readonly RenderingContext _vkContext;
        private readonly RenderPassManager _renderPassManager;
        private readonly GraphicsEngine _graphicsEngine;
        private readonly IInputContext _input;
        private VkFramebuffer[] _framebuffers;
        private VkDescriptorPool _descriptorPool;
        private PipelineLayout _pipelineLayout;
        private DescriptorSetLayout _descriptorSetLayout;
        private DescriptorSet _descriptorSet;
        private readonly VkBuffer?[] _vertexBuffers;
        private readonly VkBuffer?[] _indexBuffers;
        private bool _frameBegun;
        private VkRenderPass _renderPass;
        private Pipeline _pipeline;
        private Queue<char> _pressedChars = new Queue<char>();
        private ulong _bufferMemoryAlignment;
        private Texture _fontTexture;

        public unsafe ImGuiController(RenderingContext vkContext, GraphicsEngine graphicsEngine, RenderPassManager renderPassManager, IInputContext inputContext, uint width, uint height)
        {
            _vkContext = vkContext;
            _renderPassManager = renderPassManager;
            _graphicsEngine = graphicsEngine;
            _input = inputContext;
            ImGui.CreateContext();
            var io = ImGui.GetIO();
            io.Fonts.AddFontDefault();
            _vertexBuffers = new VkBuffer[_vkContext.MaxFramesPerFlight];
            _indexBuffers = new VkBuffer[_vkContext.MaxFramesPerFlight];
            CreateDescriptorPool();
            CreateDescriptorSetLayout();
            CreateFontResources();
            CreateDescriptorSet();
            CreateDefaultRenderPass();
            CreateDeviceObjects();
            graphicsEngine.Swapchain.OnSwapchainRecreate += CreateFramebuffers;
            CreateFramebuffers(graphicsEngine.Swapchain);
            ApplyDarkTheme();
            ImGui.GetIO().DisplaySize = new Vector2(width, height);
            _input.Keyboards[0].KeyChar +=(s,c)  => PressChar(c);


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

            while(_pressedChars.Count > 0)
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

        private unsafe void CreateDefaultRenderPass()
        {
            var colorAttachment = new AttachmentDescription
            {
                Format = _graphicsEngine.Swapchain.Format,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.Clear,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.PresentSrcKhr
            };

            var colorAttachmentReference = new AttachmentReference
            {
                Attachment = 0,
                Layout = ImageLayout.ColorAttachmentOptimal,
               
            };


           /* var depthAttachment = new AttachmentDescription
            {
                Format = _graphicsEngine.Swapchain.DepthFormat,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.DontCare,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
            };

            var depthAttachmentReference = new AttachmentReference
            {
                Attachment = 1,
                Layout = ImageLayout.DepthStencilAttachmentOptimal
            };*/

            var description = new SubpassDescription
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                PColorAttachments = &colorAttachmentReference,
                //PDepthStencilAttachment = &depthAttachmentReference,
            };

            var dependency = new SubpassDependency
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit ,
                SrcAccessMask = AccessFlags.None,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                DstAccessMask = AccessFlags.ColorAttachmentReadBit,
            };

            _renderPass = _renderPassManager.CreateRenderPass(ImguiRenderPass, [description], [colorAttachment], [dependency]);
        }


        private unsafe void CreateFramebuffers(VkSwapchain swapchain)
        {
            var swapChainImageViews = swapchain.SwapChainImageViews;
            var swapChainDepthImageView = swapchain.DepthImageView;
            var swapChainExtent = swapchain.Extent;

            if (_framebuffers is not null)
            {
                foreach (var item in _framebuffers)
                {
                    item.Dispose();
                }
            }
            else
            {
                _framebuffers = new VkFramebuffer[swapChainImageViews.Length];
            }
           

            for (int i = 0; i < swapChainImageViews.Length; i++)
            {
                var attachments = new ImageView[] { swapChainImageViews[i].VkObjectNative  };
                fixed (ImageView* attachmentsPtr = attachments)
                {
                    var framebufferInfo = new FramebufferCreateInfo
                    {
                        SType = StructureType.FramebufferCreateInfo,
                        RenderPass = _renderPass,
                        AttachmentCount = 1,
                        PAttachments = attachmentsPtr,
                        Width = swapChainExtent.Width,
                        Height = swapChainExtent.Height,
                        Layers = 1
                    };
                    var framebuffer = VkFramebuffer.Create(_vkContext, in framebufferInfo);
                    _framebuffers[i] = framebuffer;
                }
            }
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
            var clearValues = stackalloc ClearValue[] {
                new ClearValue() { Color = new ClearColorValue(.01f, .01f, .01f, 1f) },
                new ClearValue() { DepthStencil = new ClearDepthStencilValue(1f, 0u) } };
            RenderPassBeginInfo renderPassBeginInfo = new RenderPassBeginInfo()
            {
                SType =  StructureType.RenderPassBeginInfo,
                ClearValueCount = 2,
                Framebuffer = _framebuffers[_graphicsEngine.CurrentImageIndex],
                PClearValues = clearValues,
                RenderArea = new Rect2D() { Extent = swapChainExtent, Offset = new Offset2D() },
                RenderPass = _renderPass
            };
            commandBuffer.BeginRenderPass(renderPassBeginInfo, SubpassContents.Inline);

            ref var vertexBuffer = ref _vertexBuffers[_graphicsEngine.CurrentImageIndex];
            ref var indexBuffer = ref _indexBuffers![_graphicsEngine.CurrentImageIndex];
            if (drawData.TotalVtxCount > 0)
            {
                // Calculate required buffer sizes
                ulong requiredVertexSize = (ulong)drawData.TotalVtxCount * (ulong)sizeof(ImDrawVert);
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
                    ImDrawList* cmd_list = drawData.CmdLists[n];
                    Unsafe.CopyBlock(vtx_dst, cmd_list->VtxBuffer.Data.ToPointer(), (uint)cmd_list->VtxBuffer.Size * (uint)sizeof(ImDrawVert));
                    Unsafe.CopyBlock(idx_dst, cmd_list->IdxBuffer.Data.ToPointer(), (uint)cmd_list->IdxBuffer.Size * sizeof(ushort));
                    vtx_dst += cmd_list->VtxBuffer.Size;
                    idx_dst += cmd_list->IdxBuffer.Size;
                }
                vertexBuffer.Flush();
                indexBuffer.Flush();
                vertexBuffer.Unmap();
                indexBuffer.Unmap();
            }

            // Setup desired Vulkan state
            RenderingContext.Vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _pipeline);
            RenderingContext.Vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 0, 1, _descriptorSet, 0, null);

            // Bind Vertex And Index Buffer:
            if (drawData.TotalVtxCount > 0)
            {
                ulong vertex_offset = 0;
                RenderingContext.Vk.CmdBindVertexBuffers(commandBuffer, 0, 1, vertexBuffer!, (ulong*)Unsafe.AsPointer(ref vertex_offset));
                RenderingContext.Vk.CmdBindIndexBuffer(commandBuffer, indexBuffer!, 0, sizeof(ushort) == 2 ? IndexType.Uint16 : IndexType.Uint32);
            }

            // Setup viewport:
            Viewport viewport;
            viewport.X = 0;
            viewport.Y = 0;
            viewport.Width = _graphicsEngine.Swapchain.Extent.Width;
            viewport.Height = _graphicsEngine.Swapchain.Extent.Height;
            viewport.MinDepth = 0.0f;
            viewport.MaxDepth = 1.0f;
            RenderingContext.Vk.CmdSetViewport(commandBuffer, 0, 1, &viewport);

            // Setup scale and translation:
            // Our visible imgui space lies from draw_data.DisplayPps (top left) to draw_data.DisplayPos+data_data.DisplaySize (bottom right). DisplayPos is (0,0) for single viewport apps.
            Span<float> scale = stackalloc float[2];
            scale[0] = 2.0f / drawData.DisplaySize.X;
            scale[1] = 2.0f / drawData.DisplaySize.Y;
            Span<float> translate = stackalloc float[2];
            translate[0] = -1.0f - drawData.DisplayPos.X * scale[0];
            translate[1] = -1.0f - drawData.DisplayPos.Y * scale[1];
            RenderingContext.Vk.CmdPushConstants(commandBuffer, _pipelineLayout, ShaderStageFlags.VertexBit, sizeof(float) * 0, sizeof(float) * 2, scale);
            RenderingContext.Vk.CmdPushConstants(commandBuffer, _pipelineLayout, ShaderStageFlags.VertexBit, sizeof(float) * 2, sizeof(float) * 2, translate);

            // Will project scissor/clipping rectangles into framebuffer space
            Vector2 clipOff = drawData.DisplayPos;         // (0,0) unless using multi-viewports
            Vector2 clipScale = drawData.FramebufferScale; // (1,1) unless using retina display which are often (2,2)

            // Render command lists
            // (Because we merged all buffers into a single one, we maintain our own offset into them)
            int vertexOffset = 0;
            int indexOffset = 0;
            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                ImDrawList* cmd_list = drawData.CmdLists[n];
                for (int cmd_i = 0; cmd_i < cmd_list->CmdBuffer.Size; cmd_i++)
                {
                    ref ImDrawCmd pcmd = ref cmd_list->CmdBuffer.Ref<ImDrawCmd>(cmd_i);
                    if (pcmd.ElemCount == 0)
                    {
                        continue;
                    }

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
                        RenderingContext.Vk.CmdSetScissor(commandBuffer, 0, 1, &scissor);

                        // Draw
                        RenderingContext.Vk.CmdDrawIndexed(commandBuffer, pcmd.ElemCount, 1, pcmd.IdxOffset + (uint)indexOffset, (int)pcmd.VtxOffset + vertexOffset, 0);
                    }
                }
                indexOffset += cmd_list->IdxBuffer.Size;
                vertexOffset += cmd_list->VtxBuffer.Size;
            }
            RenderingContext.Vk.CmdEndRenderPass(commandBuffer);
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
                new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = 1 }
            };

            var poolInfo = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                PoolSizeCount = (uint)poolSizes.Length,
                PPoolSizes = (DescriptorPoolSize*)Unsafe.AsPointer(ref poolSizes[0]),
                MaxSets = 1
            };

            _descriptorPool = VkDescriptorPool.Create(_vkContext, in poolInfo);
        }

        private unsafe void CreateDescriptorSetLayout()
        {
            var binding = new DescriptorSetLayoutBinding
            {
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit
            };

            var layoutInfo = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = 1,
                PBindings = &binding
            };

            RenderingContext.Vk.CreateDescriptorSetLayout(_vkContext.Device, in layoutInfo, default, out _descriptorSetLayout)
                .VkAssertResult("Failed to create descriptor set layout!");
        }

        private unsafe void CreateDescriptorSet()
        {
            var layout = _descriptorSetLayout;
            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _descriptorPool,
                DescriptorSetCount = 1,
                PSetLayouts = &layout
            };

            if (RenderingContext.Vk.AllocateDescriptorSets(_vkContext.Device, in allocInfo, out _descriptorSet) != Result.Success)
            {
                throw new Exception("Failed to allocate descriptor sets!");
            }

            var imageInfo = new DescriptorImageInfo
            {
                Sampler = _fontTexture.Sampler,
                ImageView = _fontTexture.ImageView,
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal
            };

            var descriptorWrite = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _descriptorSet,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.CombinedImageSampler,
                PImageInfo = &imageInfo
            };

            RenderingContext.Vk.UpdateDescriptorSets(_vkContext.Device, 1, &descriptorWrite, 0, null);

        }

        private unsafe void CreateFontResources()
        {

            var io = ImGui.GetIO();
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            io.Fonts.GetTexDataAsRGBA32(out nint pixels, out int width, out int height);
            _fontTexture = Texture.Create(_vkContext, width, height, Format.R8G8B8A8Unorm, pixels);

            // Store our identifier
            io.Fonts.SetTexID((nint)_fontTexture.Image.VkObjectNative.Handle);
        }


        private unsafe void CreateDeviceObjects()
        {
            // Create shaders
            var vertShaderModule = CreateShaderModule(ImGuiShaders.VertexShader, ShaderStageFlags.VertexBit);
            var fragShaderModule = CreateShaderModule(ImGuiShaders.FragmentShader, ShaderStageFlags.FragmentBit);

            // Create pipeline layout
            var pushConstantRange = new PushConstantRange
            {
                StageFlags = ShaderStageFlags.VertexBit,
                Offset = 0,
                Size = 4 * sizeof(float)
            };
            var setLayout = _descriptorSetLayout;
            var pipelineLayoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = &setLayout,
                PushConstantRangeCount = 1,
                PPushConstantRanges = &pushConstantRange
            };
            if (RenderingContext.Vk.CreatePipelineLayout(_vkContext.Device, in pipelineLayoutInfo, null, out _pipelineLayout) != Result.Success)
            {
                throw new Exception("Failed to create pipeline layout!");
            }

            // Create pipeline
            Span<PipelineShaderStageCreateInfo> stage = stackalloc PipelineShaderStageCreateInfo[2];
            stage[0].SType = StructureType.PipelineShaderStageCreateInfo;
            stage[0].Stage = ShaderStageFlags.VertexBit;
            stage[0].Module = vertShaderModule;
            stage[0].PName = (byte*)SilkMarshal.StringToPtr("main");
            stage[1].SType = StructureType.PipelineShaderStageCreateInfo;
            stage[1].Stage = ShaderStageFlags.FragmentBit;
            stage[1].Module = fragShaderModule;
            stage[1].PName = (byte*)SilkMarshal.StringToPtr("main");

            var binding_desc = new VertexInputBindingDescription();
            binding_desc.Stride = (uint)Unsafe.SizeOf<ImDrawVert>();
            binding_desc.InputRate = VertexInputRate.Vertex;

            Span<VertexInputAttributeDescription> attribute_desc = stackalloc VertexInputAttributeDescription[3];
            attribute_desc[0].Location = 0;
            attribute_desc[0].Binding = binding_desc.Binding;
            attribute_desc[0].Format = Format.R32G32Sfloat;
            attribute_desc[0].Offset = (uint)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.pos));
            attribute_desc[1].Location = 1;
            attribute_desc[1].Binding = binding_desc.Binding;
            attribute_desc[1].Format = Format.R32G32Sfloat;
            attribute_desc[1].Offset = (uint)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.uv));
            attribute_desc[2].Location = 2;
            attribute_desc[2].Binding = binding_desc.Binding;
            attribute_desc[2].Format = Format.R8G8B8A8Unorm;
            attribute_desc[2].Offset = (uint)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.col));

            var vertex_info = new PipelineVertexInputStateCreateInfo();
            vertex_info.SType = StructureType.PipelineVertexInputStateCreateInfo;
            vertex_info.VertexBindingDescriptionCount = 1;
            vertex_info.PVertexBindingDescriptions = (VertexInputBindingDescription*)Unsafe.AsPointer(ref binding_desc);
            vertex_info.VertexAttributeDescriptionCount = 3;
            vertex_info.PVertexAttributeDescriptions = (VertexInputAttributeDescription*)Unsafe.AsPointer(ref attribute_desc[0]);

            var ia_info = new PipelineInputAssemblyStateCreateInfo();
            ia_info.SType = StructureType.PipelineInputAssemblyStateCreateInfo;
            ia_info.Topology = PrimitiveTopology.TriangleList;

            var viewport_info = new PipelineViewportStateCreateInfo();
            viewport_info.SType = StructureType.PipelineViewportStateCreateInfo;
            viewport_info.ViewportCount = 1;
            viewport_info.ScissorCount = 1;

            var raster_info = new PipelineRasterizationStateCreateInfo();
            raster_info.SType = StructureType.PipelineRasterizationStateCreateInfo;
            raster_info.PolygonMode = PolygonMode.Fill;
            raster_info.CullMode = CullModeFlags.None;
            raster_info.FrontFace = FrontFace.CounterClockwise;
            raster_info.LineWidth = 1.0f;

            var ms_info = new PipelineMultisampleStateCreateInfo();
            ms_info.SType = StructureType.PipelineMultisampleStateCreateInfo;
            ms_info.RasterizationSamples = SampleCountFlags.Count1Bit;

            var color_attachment = new PipelineColorBlendAttachmentState();
            color_attachment.BlendEnable = new Silk.NET.Core.Bool32(true);
            color_attachment.SrcColorBlendFactor = BlendFactor.SrcAlpha;
            color_attachment.DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha;
            color_attachment.ColorBlendOp = BlendOp.Add;
            color_attachment.SrcAlphaBlendFactor = BlendFactor.One;
            color_attachment.DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha;
            color_attachment.AlphaBlendOp = BlendOp.Add;
            color_attachment.ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit;

            var depth_info = new PipelineDepthStencilStateCreateInfo();
            depth_info.SType = StructureType.PipelineDepthStencilStateCreateInfo;

            var blend_info = new PipelineColorBlendStateCreateInfo();
            blend_info.SType = StructureType.PipelineColorBlendStateCreateInfo;
            blend_info.AttachmentCount = 1;
            blend_info.PAttachments = (PipelineColorBlendAttachmentState*)Unsafe.AsPointer(ref color_attachment);

            Span<DynamicState> dynamic_states = stackalloc DynamicState[] { DynamicState.Viewport, DynamicState.Scissor };
            var dynamic_state = new PipelineDynamicStateCreateInfo();
            dynamic_state.SType = StructureType.PipelineDynamicStateCreateInfo;
            dynamic_state.DynamicStateCount = (uint)dynamic_states.Length;
            dynamic_state.PDynamicStates = (DynamicState*)Unsafe.AsPointer(ref dynamic_states[0]);

            var pipelineInfo = new GraphicsPipelineCreateInfo();
            pipelineInfo.SType = StructureType.GraphicsPipelineCreateInfo;
            pipelineInfo.Flags = default;
            pipelineInfo.StageCount = 2;
            pipelineInfo.PStages = (PipelineShaderStageCreateInfo*)Unsafe.AsPointer(ref stage[0]);
            pipelineInfo.PVertexInputState = (PipelineVertexInputStateCreateInfo*)Unsafe.AsPointer(ref vertex_info);
            pipelineInfo.PInputAssemblyState = (PipelineInputAssemblyStateCreateInfo*)Unsafe.AsPointer(ref ia_info);
            pipelineInfo.PViewportState = (PipelineViewportStateCreateInfo*)Unsafe.AsPointer(ref viewport_info);
            pipelineInfo.PRasterizationState = (PipelineRasterizationStateCreateInfo*)Unsafe.AsPointer(ref raster_info);
            pipelineInfo.PMultisampleState = (PipelineMultisampleStateCreateInfo*)Unsafe.AsPointer(ref ms_info);
            pipelineInfo.PDepthStencilState = (PipelineDepthStencilStateCreateInfo*)Unsafe.AsPointer(ref depth_info);
            pipelineInfo.PColorBlendState = (PipelineColorBlendStateCreateInfo*)Unsafe.AsPointer(ref blend_info);
            pipelineInfo.PDynamicState = (PipelineDynamicStateCreateInfo*)Unsafe.AsPointer(ref dynamic_state);
            pipelineInfo.Layout = _pipelineLayout;
            pipelineInfo.RenderPass = _renderPass;
            pipelineInfo.Subpass = 0;
            if (RenderingContext.Vk.CreateGraphicsPipelines(_vkContext.Device, default, 1, in pipelineInfo, default, out _pipeline) != Result.Success)
            {
                throw new Exception($"Unable to create the pipeline");
            }

            SilkMarshal.Free((nint)stage[0].PName);
            SilkMarshal.Free((nint)stage[1].PName);
        }

        private unsafe ShaderModule CreateShaderModule(uint[] code, ShaderStageFlags stage)
        {
            return VkShaderModule.Create(_vkContext, code, stage);
        }
        public static void ApplyDarkTheme()
        {
            var style = ImGui.GetStyle();
            var colors = style.Colors;

            style.WindowPadding = new Vector2(15, 15);
            style.WindowRounding = 5.0f;
            style.FramePadding = new Vector2(5, 5);
            style.FrameRounding = 4.0f;
            style.ItemSpacing = new Vector2(12, 8);
            style.ItemInnerSpacing = new Vector2(8, 6);
            style.IndentSpacing = 25.0f;
            style.ScrollbarSize = 15.0f;
            style.ScrollbarRounding = 9.0f;
            style.GrabMinSize = 5.0f;
            style.GrabRounding = 3.0f;

            colors[(int)ImGuiCol.Text] = new Vector4(0.80f, 0.80f, 0.83f, 1.00f);
            colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.24f, 0.23f, 0.29f, 1.00f);
            colors[(int)ImGuiCol.WindowBg] = new Vector4(0.06f, 0.05f, 0.07f, 1.00f);
            colors[(int)ImGuiCol.ChildBg] = new Vector4(0.07f, 0.07f, 0.09f, 1.00f);
            colors[(int)ImGuiCol.PopupBg] = new Vector4(0.07f, 0.07f, 0.09f, 1.00f);
            colors[(int)ImGuiCol.Border] = new Vector4(0.80f, 0.80f, 0.83f, 0.88f);
            colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.92f, 0.91f, 0.88f, 0.00f);
            colors[(int)ImGuiCol.FrameBg] = new Vector4(0.10f, 0.09f, 0.12f, 1.00f);
            colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.24f, 0.23f, 0.29f, 1.00f);
            colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.56f, 0.56f, 0.58f, 1.00f);
            colors[(int)ImGuiCol.TitleBg] = new Vector4(0.10f, 0.09f, 0.12f, 1.00f);
            colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(1.00f, 0.98f, 0.95f, 0.75f);
            colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.07f, 0.07f, 0.09f, 1.00f);
            colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.10f, 0.09f, 0.12f, 1.00f);
            colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.10f, 0.09f, 0.12f, 1.00f);
            colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.80f, 0.80f, 0.83f, 0.31f);
            colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.56f, 0.56f, 0.58f, 1.00f);
            colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.06f, 0.05f, 0.07f, 1.00f);
            colors[(int)ImGuiCol.CheckMark] = new Vector4(0.80f, 0.80f, 0.83f, 0.31f);
            colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.80f, 0.80f, 0.83f, 0.31f);
            colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.06f, 0.05f, 0.07f, 1.00f);
            colors[(int)ImGuiCol.Button] = new Vector4(0.10f, 0.09f, 0.12f, 1.00f);
            colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.24f, 0.23f, 0.29f, 1.00f);
            colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.56f, 0.56f, 0.58f, 1.00f);
            colors[(int)ImGuiCol.Header] = new Vector4(0.10f, 0.09f, 0.12f, 1.00f);
            colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.56f, 0.56f, 0.58f, 1.00f);
            colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.06f, 0.05f, 0.07f, 1.00f);
            colors[(int)ImGuiCol.Separator] = new Vector4(0.56f, 0.56f, 0.58f, 1.00f);
            colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.24f, 0.23f, 0.29f, 1.00f);
            colors[(int)ImGuiCol.SeparatorActive] = new Vector4(0.56f, 0.56f, 0.58f, 1.00f);
            colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
            colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.56f, 0.56f, 0.58f, 1.00f);
            colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(0.06f, 0.05f, 0.07f, 1.00f);
            colors[(int)ImGuiCol.PlotLines] = new Vector4(0.40f, 0.50f, 0.40f, 0.63f);
            colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(0.40f, 0.50f, 0.40f, 0.63f);
            colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.40f, 0.50f, 0.40f, 0.63f);
            colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(0.25f, 1.00f, 0.00f, 1.00f);
            colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.25f, 1.00f, 0.00f, 0.43f);
            colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(1.00f, 0.98f, 0.95f, 0.73f);

            colors[(int)ImGuiCol.Tab] = new Vector4(0.10f, 0.09f, 0.12f, 1.00f);
            colors[(int)ImGuiCol.TabHovered] = new Vector4(0.56f, 0.56f, 0.58f, 1.00f);
            colors[(int)ImGuiCol.TabActive] = new Vector4(0.28f, 0.28f, 0.28f, 1.00f);
            colors[(int)ImGuiCol.TabUnfocused] = new Vector4(0.07f, 0.10f, 0.15f, 0.97f);
            colors[(int)ImGuiCol.TabUnfocusedActive] = new Vector4(0.07f, 0.10f, 0.15f, 0.97f);
            colors[(int)ImGuiCol.DockingPreview] = new Vector4(0.26f, 0.59f, 0.98f, 0.70f);
            colors[(int)ImGuiCol.DockingEmptyBg] = new Vector4(0.20f, 0.20f, 0.20f, 1.00f);
            colors[(int)ImGuiCol.DragDropTarget] = new Vector4(1.00f, 1.00f, 0.00f, 0.90f);
            colors[(int)ImGuiCol.NavHighlight] = new Vector4(0.26f, 0.59f, 0.98f, 1.00f);
            colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(1.00f, 1.00f, 1.00f, 0.70f);
            colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.80f, 0.80f, 0.80f, 0.20f);
            colors[(int)ImGuiCol.TabUnfocused] = new Vector4(0.07f, 0.10f, 0.15f, 0.97f);
        }




        public void Dispose()
        {

        }     
    }
}