using ImGuiNET;

using RockEngine.Vulkan.GUI;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Core.Native;
using Silk.NET.GLFW;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.SDL;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using BlendFactor = Silk.NET.Vulkan.BlendFactor;
using Buffer = Silk.NET.Vulkan.Buffer;
using Image = Silk.NET.Vulkan.Image;
using ImageView = Silk.NET.Vulkan.ImageView;
using Sampler = Silk.NET.Vulkan.Sampler;

namespace RockEngine.Vulkan.Rendering.ImGuiRender
{
    public class ImGuiController : IDisposable
    {
        private IWindow _view;
        private IInputContext _input;
        private Device _device;
        private PhysicalDevice _physicalDevice;
        private bool _frameBegun;
        private readonly List<char> _pressedChars = new List<char>();
        private readonly VulkanContext _vkContext;
        private IKeyboard _keyboard;
        private DescriptorPool _descriptorPool;
        private RenderPass _renderPass;
        private int _windowWidth;
        private int _windowHeight;
        private int _swapChainImageCt;
        private Sampler _fontSampler;
        private DescriptorSetLayout _descriptorSetLayout;
        private DescriptorSet _descriptorSet;
        private PipelineLayout _pipelineLayout;
        private ShaderModule _shaderModuleVert;
        private ShaderModule _shaderModuleFrag;
        private Pipeline _pipeline;
        private WindowRenderBuffers _mainWindowRenderBuffers;
        private GlobalMemory _frameRenderBuffers;
        private DeviceMemory _fontMemory;
        private Image _fontImage;
        private ImageView _fontView;
        private ulong _bufferMemoryAlignment = 256;
        private bool _platformInterfaceInitialized;
        private List<IWindow> _windows = new List<IWindow>();
        private ImGuiWindow _mainViewportWindow;
        private Platform_CreateWindow _createWindow;
        private Platform_DestroyWindow _destroyWindow;
        private Platform_GetWindowPos _getWindowPos;
        private Platform_ShowWindow _showWindow;
        private Platform_SetWindowPos _setWindowPos;
        private Platform_SetWindowSize _setWindowSize;
        private Platform_GetWindowSize _getWindowSize;
        private Platform_SetWindowFocus _setWindowFocus;
        private Platform_GetWindowFocus _getWindowFocus;
        private Platform_GetWindowMinimized _getWindowMinimized;
        private Platform_SetWindowTitle _setWindowTitle;
        private Platform_SetWindowAlphaDelegate _setWindowAlpha;
        private Sdl sdlApi;
        /// <summary>
        /// Constructs a new ImGuiController.
        /// </summary>
        /// <param name="vk">The vulkan api instance</param>
        /// <param name="view">Window view</param>
        /// <param name="input">Input context</param>
        /// <param name="physicalDevice">The physical device instance in use</param>
        /// <param name="graphicsFamilyIndex">The graphics family index corresponding to the graphics queue</param>
        /// <param name="swapChainImageCt">The number of images used in the swap chain</param>
        /// <param name="swapChainFormat">The image format used by the swap chain</param>
        /// <param name="depthBufferFormat">The image formate used by the depth buffer, or null if no depth buffer is used</param>
        public ImGuiController(VulkanContext vkContext, IWindow view, IInputContext input, PhysicalDevice physicalDevice, uint graphicsFamilyIndex, int swapChainImageCt, Format swapChainFormat, Format? depthBufferFormat, RenderPass renderPass)
        {
            _vkContext = vkContext;
            sdlApi = Sdl.GetApi();
            var context = ImGui.CreateContext();
            ImGui.SetCurrentContext(context);
            var io = ImGui.GetIO();
            io.Fonts.AddFontDefault();

            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;



            Init(vkContext.Api, view, input, physicalDevice, graphicsFamilyIndex, swapChainImageCt, swapChainFormat, depthBufferFormat, renderPass);

            SetKeyMappings();

            SetPerFrameImGuiData(1f / 60f);
            InitPlatformInterface();

            io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;
            io.BackendFlags |= ImGuiBackendFlags.HasSetMousePos;
            io.BackendFlags |= ImGuiBackendFlags.PlatformHasViewports;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasViewports;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

            BeginFrame();

         
        }

        private unsafe void Init(Vk vk, IWindow view, IInputContext input, PhysicalDevice physicalDevice, uint graphicsFamilyIndex, int swapChainImageCt, Format swapChainFormat, Format? depthBufferFormat, RenderPass rp)
        {
            _view = view;
            _input = input;
            _physicalDevice = physicalDevice;
            _windowWidth = view.Size.X;
            _windowHeight = view.Size.Y;
            _swapChainImageCt = swapChainImageCt;
            _renderPass = rp;

            if (swapChainImageCt < 2)
            {
                throw new Exception($"Swap chain image count must be >= 2");
            }

            if (!_vkContext.Api.CurrentDevice.HasValue)
            {
                throw new InvalidOperationException("vk.CurrentDevice is null. _vkContext.Api.CurrentDevice must be set to the current device.");
            }

            _device = _vkContext.Api.CurrentDevice.Value;

            // Set default style
            ImGui.StyleColorsDark();

            // Create the descriptor pool for ImGui
            Span<DescriptorPoolSize> poolSizes = [new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 1)];
            var descriptorPool = new DescriptorPoolCreateInfo();
            descriptorPool.SType = StructureType.DescriptorPoolCreateInfo;
            descriptorPool.PoolSizeCount = (uint)poolSizes.Length;
            descriptorPool.PPoolSizes = (DescriptorPoolSize*)Unsafe.AsPointer(ref poolSizes.GetPinnableReference());
            descriptorPool.MaxSets = 1;
            if (_vkContext.Api.CreateDescriptorPool(_device, in descriptorPool, default, out _descriptorPool) != Result.Success)
            {
                throw new Exception($"Unable to create descriptor pool");
            }

            // Create the render pass
            var colorAttachment = new AttachmentDescription
            {
                Format = swapChainFormat,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Load,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = AttachmentLoadOp.Load == AttachmentLoadOp.Clear ? ImageLayout.Undefined : ImageLayout.PresentSrcKhr,
                FinalLayout = ImageLayout.PresentSrcKhr
            };

            var colorAttachmentRef = new AttachmentReference
            {
                Attachment = 0,
                Layout = ImageLayout.ColorAttachmentOptimal
            };

            var subpass = new SubpassDescription
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                PColorAttachments = (AttachmentReference*)Unsafe.AsPointer(ref colorAttachmentRef)
            };

            //Span<AttachmentDescription> attachments = stackalloc AttachmentDescription[] { colorAttachment };
            //var depthAttachment = new AttachmentDescription();
            //var depthAttachmentRef = new AttachmentReference();
            //if (depthBufferFormat.HasValue)
            //{
            //    depthAttachment.Format = depthBufferFormat.Value;
            //    depthAttachment.Samples = SampleCountFlags.Count1Bit;
            //    depthAttachment.LoadOp = AttachmentLoadOp.Load;
            //    depthAttachment.StoreOp = AttachmentStoreOp.DontCare;
            //    depthAttachment.StencilLoadOp = AttachmentLoadOp.DontCare;
            //    depthAttachment.StencilStoreOp = AttachmentStoreOp.DontCare;
            //    depthAttachment.InitialLayout = ImageLayout.DepthStencilAttachmentOptimal;
            //    depthAttachment.FinalLayout = ImageLayout.DepthStencilAttachmentOptimal;
            //
            //    depthAttachmentRef.Attachment = 1;
            //    depthAttachmentRef.Layout = ImageLayout.DepthStencilAttachmentOptimal;
            //
            //    subpass.PDepthStencilAttachment = (AttachmentReference*)Unsafe.AsPointer(ref depthAttachmentRef);
            //
            //    attachments = stackalloc AttachmentDescription[] { colorAttachment, depthAttachment };
            //}
            //var dependency = new SubpassDependency();
            //dependency.SrcSubpass = Vk.SubpassExternal;
            //dependency.DstSubpass = 0;
            //dependency.SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit;
            //dependency.SrcAccessMask = AccessFlags.None;
            //dependency.DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit;
            //dependency.DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.ColorAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit;
            //
            //var renderPassInfo = new RenderPassCreateInfo();
            //renderPassInfo.SType = StructureType.RenderPassCreateInfo;
            //renderPassInfo.AttachmentCount = (uint)attachments.Length;
            //renderPassInfo.PAttachments = (AttachmentDescription*)Unsafe.AsPointer(ref attachments.GetPinnableReference());
            //renderPassInfo.SubpassCount = 1;
            //renderPassInfo.PSubpasses = (SubpassDescription*)Unsafe.AsPointer(ref subpass);
            //renderPassInfo.DependencyCount = 1;
            //renderPassInfo.PDependencies = (SubpassDependency*)Unsafe.AsPointer(ref dependency);
            //
            //if (_vkContext.Api.CreateRenderPass(_device, in renderPassInfo, default, out _renderPass) != Result.Success)
            //{
            //    throw new Exception($"Failed to create render pass");
            //}

            var info = new SamplerCreateInfo();
            info.SType = StructureType.SamplerCreateInfo;
            info.MagFilter = Filter.Linear;
            info.MinFilter = Filter.Linear;
            info.MipmapMode = SamplerMipmapMode.Linear;
            info.AddressModeU = SamplerAddressMode.Repeat;
            info.AddressModeV = SamplerAddressMode.Repeat;
            info.AddressModeW = SamplerAddressMode.Repeat;
            info.MinLod = -1000;
            info.MaxLod = 1000;
            info.MaxAnisotropy = 1.0f;
            if (vk.CreateSampler(_device, in info, default, out _fontSampler) != Result.Success)
            {
                throw new Exception($"Unable to create sampler");
            }

            var sampler = _fontSampler;

            var binding = new DescriptorSetLayoutBinding();
            binding.DescriptorType = DescriptorType.CombinedImageSampler;
            binding.DescriptorCount = 1;
            binding.StageFlags = ShaderStageFlags.FragmentBit;
            binding.PImmutableSamplers = (Sampler*)Unsafe.AsPointer(ref sampler);

            var descriptorInfo = new DescriptorSetLayoutCreateInfo();
            descriptorInfo.SType = StructureType.DescriptorSetLayoutCreateInfo;
            descriptorInfo.BindingCount = 1;
            descriptorInfo.PBindings = (DescriptorSetLayoutBinding*)Unsafe.AsPointer(ref binding);
            if (vk.CreateDescriptorSetLayout(_device, ref descriptorInfo, default, out _descriptorSetLayout) != Result.Success)
            {
                throw new Exception($"Unable to create descriptor set layout");
            }

            fixed (DescriptorSetLayout* pg_DescriptorSetLayout = &_descriptorSetLayout)
            {
                var alloc_info = new DescriptorSetAllocateInfo();
                alloc_info.SType = StructureType.DescriptorSetAllocateInfo;
                alloc_info.DescriptorPool = _descriptorPool;
                alloc_info.DescriptorSetCount = 1;
                alloc_info.PSetLayouts = pg_DescriptorSetLayout;
                if (vk.AllocateDescriptorSets(_device, ref alloc_info, out _descriptorSet) != Result.Success)
                {
                    throw new Exception($"Unable to create descriptor sets");
                }
            }

            var vertPushConst = new PushConstantRange();
            vertPushConst.StageFlags = ShaderStageFlags.VertexBit;
            vertPushConst.Offset = sizeof(float) * 0;
            vertPushConst.Size = sizeof(float) * 4;

            var set_layout = _descriptorSetLayout;
            var layout_info = new PipelineLayoutCreateInfo();
            layout_info.SType = StructureType.PipelineLayoutCreateInfo;
            layout_info.SetLayoutCount = 1;
            layout_info.PSetLayouts = (DescriptorSetLayout*)Unsafe.AsPointer(ref set_layout);
            layout_info.PushConstantRangeCount = 1;
            layout_info.PPushConstantRanges = (PushConstantRange*)Unsafe.AsPointer(ref vertPushConst);
            if (vk.CreatePipelineLayout(_device, ref layout_info, default, out _pipelineLayout) != Result.Success)
            {
                throw new Exception($"Unable to create the descriptor set layout");
            }

            // Create the shader modules
            if (_shaderModuleVert.Handle == default)
            {
                fixed (uint* vertShaderBytes = &ImGuiShaders.VertexShader[0])
                {
                    var vert_info = new ShaderModuleCreateInfo();
                    vert_info.SType = StructureType.ShaderModuleCreateInfo;
                    vert_info.CodeSize = (nuint)ImGuiShaders.VertexShader.Length * sizeof(uint);
                    vert_info.PCode = vertShaderBytes;
                    if (vk.CreateShaderModule(_device, ref vert_info, default, out _shaderModuleVert) != Result.Success)
                    {
                        throw new Exception($"Unable to create the vertex shader");
                    }
                }
            }
            if (_shaderModuleFrag.Handle == default)
            {
                fixed (uint* fragShaderBytes = &ImGuiShaders.FragmentShader[0])
                {
                    var frag_info = new ShaderModuleCreateInfo();
                    frag_info.SType = StructureType.ShaderModuleCreateInfo;
                    frag_info.CodeSize = (nuint)ImGuiShaders.FragmentShader.Length * sizeof(uint);
                    frag_info.PCode = fragShaderBytes;
                    if (vk.CreateShaderModule(_device, ref frag_info, default, out _shaderModuleFrag) != Result.Success)
                    {
                        throw new Exception($"Unable to create the fragment shader");
                    }
                }
            }

            // Create the pipeline
            Span<PipelineShaderStageCreateInfo> stage = stackalloc PipelineShaderStageCreateInfo[2];
            stage[0].SType = StructureType.PipelineShaderStageCreateInfo;
            stage[0].Stage = ShaderStageFlags.VertexBit;
            stage[0].Module = _shaderModuleVert;
            stage[0].PName = (byte*)SilkMarshal.StringToPtr("main");
            stage[1].SType = StructureType.PipelineShaderStageCreateInfo;
            stage[1].Stage = ShaderStageFlags.FragmentBit;
            stage[1].Module = _shaderModuleFrag;
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
            if (vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, default, out _pipeline) != Result.Success)
            {
                throw new Exception($"Unable to create the pipeline");
            }

            SilkMarshal.Free((nint)stage[0].PName);
            SilkMarshal.Free((nint)stage[1].PName);

            // Initialise ImGui Vulkan adapter
            var io = ImGui.GetIO();
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            io.Fonts.GetTexDataAsRGBA32(out nint pixels, out int width, out int height);
            ulong upload_size = (ulong)(width * height * 4 * sizeof(byte));

            // Submit one-time command to create the fonts texture
            var poolInfo = new CommandPoolCreateInfo();
            poolInfo.SType = StructureType.CommandPoolCreateInfo;
            poolInfo.QueueFamilyIndex = graphicsFamilyIndex;
            if (_vkContext.Api.CreateCommandPool(_device, in poolInfo, null, out var commandPool) != Result.Success)
            {
                throw new Exception("failed to create command pool!");
            }

            var allocInfo = new CommandBufferAllocateInfo();
            allocInfo.SType = StructureType.CommandBufferAllocateInfo;
            allocInfo.CommandPool = commandPool;
            allocInfo.Level = CommandBufferLevel.Primary;
            allocInfo.CommandBufferCount = 1;
            if (_vkContext.Api.AllocateCommandBuffers(_device, in allocInfo, out var commandBuffer) != Result.Success)
            {
                throw new Exception($"Unable to allocate command buffers");
            }

            var beginInfo = new CommandBufferBeginInfo();
            beginInfo.SType = StructureType.CommandBufferBeginInfo;
            beginInfo.Flags = CommandBufferUsageFlags.OneTimeSubmitBit;
            if (_vkContext.Api.BeginCommandBuffer(commandBuffer, in beginInfo) != Result.Success)
            {
                throw new Exception($"Failed to begin a command buffer");
            }

            var imageInfo = new ImageCreateInfo();
            imageInfo.SType = StructureType.ImageCreateInfo;
            imageInfo.ImageType = ImageType.Type2D;
            imageInfo.Format = Format.R8G8B8A8Unorm;
            imageInfo.Extent.Width = (uint)width;
            imageInfo.Extent.Height = (uint)height;
            imageInfo.Extent.Depth = 1;
            imageInfo.MipLevels = 1;
            imageInfo.ArrayLayers = 1;
            imageInfo.Samples = SampleCountFlags.Count1Bit;
            imageInfo.Tiling = ImageTiling.Optimal;
            imageInfo.Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit;
            imageInfo.SharingMode = SharingMode.Exclusive;
            imageInfo.InitialLayout = ImageLayout.Undefined;
            if (_vkContext.Api.CreateImage(_device, in imageInfo, default, out _fontImage) != Result.Success)
            {
                throw new Exception($"Failed to create font image");
            }
            _vkContext.Api.GetImageMemoryRequirements(_device, _fontImage, out var fontReq);
            var fontAllocInfo = new MemoryAllocateInfo();
            fontAllocInfo.SType = StructureType.MemoryAllocateInfo;
            fontAllocInfo.AllocationSize = fontReq.Size;
            fontAllocInfo.MemoryTypeIndex = GetMemoryTypeIndex(vk, MemoryPropertyFlags.DeviceLocalBit, fontReq.MemoryTypeBits);
            if (_vkContext.Api.AllocateMemory(_device, &fontAllocInfo, default, out _fontMemory) != Result.Success)
            {
                throw new Exception($"Failed to allocate device memory");
            }
            if (_vkContext.Api.BindImageMemory(_device, _fontImage, _fontMemory, 0) != Result.Success)
            {
                throw new Exception($"Failed to bind device memory");
            }

            var imageViewInfo = new ImageViewCreateInfo();
            imageViewInfo.SType = StructureType.ImageViewCreateInfo;
            imageViewInfo.Image = _fontImage;
            imageViewInfo.ViewType = ImageViewType.Type2D;
            imageViewInfo.Format = Format.R8G8B8A8Unorm;
            imageViewInfo.SubresourceRange.AspectMask = ImageAspectFlags.ColorBit;
            imageViewInfo.SubresourceRange.LevelCount = 1;
            imageViewInfo.SubresourceRange.LayerCount = 1;
            if (_vkContext.Api.CreateImageView(_device, &imageViewInfo, default, out _fontView) != Result.Success)
            {
                throw new Exception($"Failed to create an image view");
            }

            var descImageInfo = new DescriptorImageInfo();
            descImageInfo.Sampler = _fontSampler;
            descImageInfo.ImageView = _fontView;
            descImageInfo.ImageLayout = ImageLayout.ShaderReadOnlyOptimal;
            var writeDescriptors = new WriteDescriptorSet();
            writeDescriptors.SType = StructureType.WriteDescriptorSet;
            writeDescriptors.DstSet = _descriptorSet;
            writeDescriptors.DescriptorCount = 1;
            writeDescriptors.DescriptorType = DescriptorType.CombinedImageSampler;
            writeDescriptors.PImageInfo = (DescriptorImageInfo*)Unsafe.AsPointer(ref descImageInfo);
            _vkContext.Api.UpdateDescriptorSets(_device, 1, in writeDescriptors, 0, default);

            // Create the Upload Buffer:
            var bufferInfo = new BufferCreateInfo();
            bufferInfo.SType = StructureType.BufferCreateInfo;
            bufferInfo.Size = upload_size;
            bufferInfo.Usage = BufferUsageFlags.TransferSrcBit;
            bufferInfo.SharingMode = SharingMode.Exclusive;
            if (_vkContext.Api.CreateBuffer(_device, in bufferInfo, default, out var uploadBuffer) != Result.Success)
            {
                throw new Exception($"Failed to create a device buffer");
            }

            _vkContext.Api.GetBufferMemoryRequirements(_device, uploadBuffer, out var uploadReq);
            _bufferMemoryAlignment = _bufferMemoryAlignment > uploadReq.Alignment ? _bufferMemoryAlignment : uploadReq.Alignment;

            var uploadAllocInfo = new MemoryAllocateInfo();
            uploadAllocInfo.SType = StructureType.MemoryAllocateInfo;
            uploadAllocInfo.AllocationSize = uploadReq.Size;
            uploadAllocInfo.MemoryTypeIndex = GetMemoryTypeIndex(vk, MemoryPropertyFlags.HostVisibleBit, uploadReq.MemoryTypeBits);
            if (_vkContext.Api.AllocateMemory(_device, in uploadAllocInfo, default, out var uploadBufferMemory) != Result.Success)
            {
                throw new Exception($"Failed to allocate device memory");
            }
            if (_vkContext.Api.BindBufferMemory(_device, uploadBuffer, uploadBufferMemory, 0) != Result.Success)
            {
                throw new Exception($"Failed to bind device memory");
            }

            void* map = null;
            if (_vkContext.Api.MapMemory(_device, uploadBufferMemory, 0, upload_size, 0, &map) != Result.Success)
            {
                throw new Exception($"Failed to map device memory");
            }
            Unsafe.CopyBlock(map, pixels.ToPointer(), (uint)upload_size);

            var range = new MappedMemoryRange();
            range.SType = StructureType.MappedMemoryRange;
            range.Memory = uploadBufferMemory;
            range.Size = upload_size;
            if (_vkContext.Api.FlushMappedMemoryRanges(_device, 1, in range) != Result.Success)
            {
                throw new Exception($"Failed to flush memory to device");
            }
            _vkContext.Api.UnmapMemory(_device, uploadBufferMemory);

            const uint VK_QUEUE_FAMILY_IGNORED = ~0U;

            var copyBarrier = new ImageMemoryBarrier();
            copyBarrier.SType = StructureType.ImageMemoryBarrier;
            copyBarrier.DstAccessMask = AccessFlags.TransferWriteBit;
            copyBarrier.OldLayout = ImageLayout.Undefined;
            copyBarrier.NewLayout = ImageLayout.TransferDstOptimal;
            copyBarrier.SrcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            copyBarrier.DstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            copyBarrier.Image = _fontImage;
            copyBarrier.SubresourceRange.AspectMask = ImageAspectFlags.ColorBit;
            copyBarrier.SubresourceRange.LevelCount = 1;
            copyBarrier.SubresourceRange.LayerCount = 1;
            _vkContext.Api.CmdPipelineBarrier(commandBuffer, PipelineStageFlags.HostBit, PipelineStageFlags.TransferBit, 0, 0, default, 0, default, 1, in copyBarrier);

            var region = new BufferImageCopy();
            region.ImageSubresource.AspectMask = ImageAspectFlags.ColorBit;
            region.ImageSubresource.LayerCount = 1;
            region.ImageExtent.Width = (uint)width;
            region.ImageExtent.Height = (uint)height;
            region.ImageExtent.Depth = 1;
            _vkContext.Api.CmdCopyBufferToImage(commandBuffer, uploadBuffer, _fontImage, ImageLayout.TransferDstOptimal, 1, &region);

            var use_barrier = new ImageMemoryBarrier();
            use_barrier.SType = StructureType.ImageMemoryBarrier;
            use_barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            use_barrier.DstAccessMask = AccessFlags.ShaderReadBit;
            use_barrier.OldLayout = ImageLayout.TransferDstOptimal;
            use_barrier.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
            use_barrier.SrcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            use_barrier.DstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            use_barrier.Image = _fontImage;
            use_barrier.SubresourceRange.AspectMask = ImageAspectFlags.ColorBit;
            use_barrier.SubresourceRange.LevelCount = 1;
            use_barrier.SubresourceRange.LayerCount = 1;
            _vkContext.Api.CmdPipelineBarrier(commandBuffer, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit, 0, 0, default, 0, default, 1, in use_barrier);

            // Store our identifier
            io.Fonts.SetTexID((nint)_fontImage.Handle);

            if (_vkContext.Api.EndCommandBuffer(commandBuffer) != Result.Success)
            {
                throw new Exception($"Failed to begin a command buffer");
            }

            _vkContext.Api.GetDeviceQueue(_device, graphicsFamilyIndex, 0, out var graphicsQueue);

            var submitInfo = new SubmitInfo();
            submitInfo.SType = StructureType.SubmitInfo;
            submitInfo.CommandBufferCount = 1;
            submitInfo.PCommandBuffers = (CommandBuffer*)Unsafe.AsPointer(ref commandBuffer);
            if (_vkContext.Api.QueueSubmit(graphicsQueue, 1, in submitInfo, default) != Result.Success)
            {
                throw new Exception($"Failed to begin a command buffer");
            }

            if (_vkContext.Api.QueueWaitIdle(graphicsQueue) != Result.Success)
            {
                throw new Exception($"Failed to begin a command buffer");
            }

            _vkContext.Api.DestroyBuffer(_device, uploadBuffer, default);
            _vkContext.Api.FreeMemory(_device, uploadBufferMemory, default);
            _vkContext.Api.DestroyCommandPool(_device, commandPool, default);
        }

        private uint GetMemoryTypeIndex(Vk vk, MemoryPropertyFlags properties, uint type_bits)
        {
            vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out var prop);
            for (int i = 0; i < prop.MemoryTypeCount; i++)
            {
                if ((prop.MemoryTypes[i].PropertyFlags & properties) == properties && (type_bits & 1u << i) != 0)
                {
                    return (uint)i;
                }
            }
            return 0xFFFFFFFF; // Unable to find memoryType
        }

        private unsafe void BeginFrame()
        {
            _keyboard = _input.Keyboards[0];
            _view.Resize += WindowResized;
            _keyboard.KeyChar += OnKeyChar;


            ImGui.NewFrame();
            _frameBegun = true;
        }


        private void OnKeyChar(IKeyboard arg1, char arg2)
        {
            _pressedChars.Add(arg2);
        }

        private void WindowResized(Vector2D<int> size)
        {
            _windowWidth = size.X;
            _windowHeight = size.Y;
        }

        /// <summary>
        /// Renders the ImGui draw list data.
        /// </summary>
        public void RenderAsync(CommandBuffer commandBuffer, Extent2D swapChainExtent)
        {
            if (_frameBegun)
            {
                _frameBegun = false;

                ImGui.Render();
                RenderImDrawData(ImGui.GetDrawData(), commandBuffer, swapChainExtent);
            }
        }
        public async Task RenderWindowsAsync()
        {
            // Update and Render additional Platform Windows
            var io = ImGui.GetIO();
            if ((io.ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
            {
                ImGui.UpdatePlatformWindows();
                ImGuiPlatformIOPtr platformIO = ImGui.GetPlatformIO();
                for (int i = 1; i < platformIO.Viewports.Size; i++) // Skip first because it is mainviewport
                {
                    ImGuiViewportPtr vp = platformIO.Viewports[i];
                    ImGuiWindow window = (ImGuiWindow)GCHandle.FromIntPtr(vp.PlatformUserData).Target;

                    if (window.Ready)
                    {
                        var vpCb = await window.Render.BeginFrameAsync();
                        if (vpCb == null)
                        {
                            return;
                        }
                        window.Render.BeginSwapchainRenderPass(vpCb);
                        io.DisplaySize = new Vector2(window.Window.Size.X, window.Window.Size.Y);

                        if (_windowWidth > 0 && _windowHeight > 0)
                        {
                            io.DisplayFramebufferScale = new Vector2(window.Window.FramebufferSize.X / window.Window.Size.X, window.Window.FramebufferSize.Y / window.Window.Size.Y);
                        }
                        UpdateImGuiInput(window.Input);
                        RenderImDrawData(vp.DrawData, vpCb, window.Extent);

                        window.Render.EndSwapchainRenderPass(vpCb);
                        window.Render.EndFrame();
                    }
                }
            }
        }


        /// <summary>
        /// Updates ImGui input and IO configuration state. Call Update() before drawing and rendering.
        /// </summary>
        public void Update(float deltaSeconds)
        {
            if (_frameBegun)
            {
                ImGui.Render();
            }

            SetPerFrameImGuiData(deltaSeconds);
            UpdateImGuiInput(_input);

            _frameBegun = true;
            ImGui.NewFrame();
        }

        private void SetPerFrameImGuiData(float deltaSeconds)
        {
            var io = ImGui.GetIO();
            io.DisplaySize = new Vector2(_windowWidth, _windowHeight);

            if (_windowWidth > 0 && _windowHeight > 0)
            {
                io.DisplayFramebufferScale = new Vector2(_view.FramebufferSize.X / _windowWidth, _view.FramebufferSize.Y / _windowHeight);
            }

            io.DeltaTime = deltaSeconds; // DeltaTime is in seconds.


        }


        private void UpdateImGuiInput(IInputContext input)
        {
            var io = ImGui.GetIO();

            var mouseState = input.Mice[0];
            var keyboardState = input.Keyboards[0];

            //io.MouseDown[0] = mouseState.IsButtonPressed(MouseButton.Left);
            //io.MouseDown[1] = mouseState.IsButtonPressed(MouseButton.Right);
            //io.MouseDown[2] = mouseState.IsButtonPressed(MouseButton.Middle);

            int x = 0, y = 0;
            var buttons = sdlApi.GetGlobalMouseState(ref x, ref y);

            io.MouseDown[0] = (buttons & 0b0001) != 0;
            io.MouseDown[1] = (buttons & 0b0010) != 0;
            io.MouseDown[2] = (buttons & 0b0100) != 0;
            io.MousePos = new Vector2(x, y);

            var wheel = mouseState.ScrollWheels[0];
            io.MouseWheel = wheel.Y;
            io.MouseWheelH = wheel.X;

            foreach (Key key in Enum.GetValues(typeof(Key)))
            {
                if (key == Key.Unknown)
                {
                    continue;
                }
                io.KeysDown[(int)key] = keyboardState.IsKeyPressed(key);
            }

            foreach (var c in _pressedChars)
            {
                io.AddInputCharacter(c);
            }

            _pressedChars.Clear();

            io.KeyCtrl = keyboardState.IsKeyPressed(Key.ControlLeft) || keyboardState.IsKeyPressed(Key.ControlRight);
            io.KeyAlt = keyboardState.IsKeyPressed(Key.AltLeft) || keyboardState.IsKeyPressed(Key.AltRight);
            io.KeyShift = keyboardState.IsKeyPressed(Key.ShiftLeft) || keyboardState.IsKeyPressed(Key.ShiftRight);
            io.KeySuper = keyboardState.IsKeyPressed(Key.SuperLeft) || keyboardState.IsKeyPressed(Key.SuperRight);
        }

        internal void PressChar(char keyChar)
        {
            _pressedChars.Add(keyChar);
        }

        private static void SetKeyMappings()
        {
            var io = ImGui.GetIO();
            io.KeyMap[(int)ImGuiKey.Tab] = (int)Key.Tab;
            io.KeyMap[(int)ImGuiKey.LeftArrow] = (int)Key.Left;
            io.KeyMap[(int)ImGuiKey.RightArrow] = (int)Key.Right;
            io.KeyMap[(int)ImGuiKey.UpArrow] = (int)Key.Up;
            io.KeyMap[(int)ImGuiKey.DownArrow] = (int)Key.Down;
            io.KeyMap[(int)ImGuiKey.PageUp] = (int)Key.PageUp;
            io.KeyMap[(int)ImGuiKey.PageDown] = (int)Key.PageDown;
            io.KeyMap[(int)ImGuiKey.Home] = (int)Key.Home;
            io.KeyMap[(int)ImGuiKey.End] = (int)Key.End;
            io.KeyMap[(int)ImGuiKey.Delete] = (int)Key.Delete;
            io.KeyMap[(int)ImGuiKey.Backspace] = (int)Key.Backspace;
            io.KeyMap[(int)ImGuiKey.Enter] = (int)Key.Enter;
            io.KeyMap[(int)ImGuiKey.Escape] = (int)Key.Escape;
            io.KeyMap[(int)ImGuiKey.A] = (int)Key.A;
            io.KeyMap[(int)ImGuiKey.C] = (int)Key.C;
            io.KeyMap[(int)ImGuiKey.V] = (int)Key.V;
            io.KeyMap[(int)ImGuiKey.X] = (int)Key.X;
            io.KeyMap[(int)ImGuiKey.Y] = (int)Key.Y;
            io.KeyMap[(int)ImGuiKey.Z] = (int)Key.Z;
        }

        private unsafe void RenderImDrawData(in ImDrawDataPtr drawDataPtr, in CommandBuffer commandBuffer, in Extent2D swapChainExtent)
        {
            int framebufferWidth = (int)(drawDataPtr.DisplaySize.X * drawDataPtr.FramebufferScale.X);
            int framebufferHeight = (int)(drawDataPtr.DisplaySize.Y * drawDataPtr.FramebufferScale.Y);
            if (framebufferWidth <= 0 || framebufferHeight <= 0)
            {
                return;
            }

            /*  ClearValue[] cv = [new ClearValue(color: new ClearColorValue() { Float32_0 = 0.2f, Float32_1 = 0.2f, Float32_2 = 0.3f, Float32_3 = 1 }),
                  new ClearValue(depthStencil:new ClearDepthStencilValue(1.0f, 0))];*//*


              var renderPassInfo = new RenderPassBeginInfo();
              renderPassInfo.SType = StructureType.RenderPassBeginInfo;
              renderPassInfo.RenderPass = _renderPass;
              renderPassInfo.Framebuffer = framebuffer;
              renderPassInfo.RenderArea.Offset = default;
              renderPassInfo.RenderArea.Extent = swapChainExtent;
              //fixed (ClearValue* pCV = cv)
              //{
                  renderPassInfo.ClearValueCount = 0; //(uint)cv.Length;
                  renderPassInfo.PClearValues = default;
                  _vkContext.Api.CmdBeginRenderPass(commandBuffer, &renderPassInfo, SubpassContents.Inline);

              //}*/


            var drawData = *drawDataPtr.NativePtr;

            // Avoid rendering when minimized, scale coordinates for retina displays (screen coordinates != framebuffer coordinates)
            int fb_width = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
            int fb_height = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
            if (fb_width <= 0 || fb_height <= 0)
            {
                return;
            }

            // Allocate array to store enough vertex/index buffers
            if (_mainWindowRenderBuffers.FrameRenderBuffers == null)
            {
                _mainWindowRenderBuffers.Index = 0;
                _mainWindowRenderBuffers.Count = (uint)_swapChainImageCt;
                _frameRenderBuffers = GlobalMemory.Allocate(sizeof(FrameRenderBuffer) * (int)_mainWindowRenderBuffers.Count);
                _mainWindowRenderBuffers.FrameRenderBuffers = _frameRenderBuffers.AsPtr<FrameRenderBuffer>();
                for (int i = 0; i < (int)_mainWindowRenderBuffers.Count; i++)
                {
                    _mainWindowRenderBuffers.FrameRenderBuffers[i].IndexBuffer.Handle = 0;
                    _mainWindowRenderBuffers.FrameRenderBuffers[i].IndexBufferSize = 0;
                    _mainWindowRenderBuffers.FrameRenderBuffers[i].IndexBufferMemory.Handle = 0;
                    _mainWindowRenderBuffers.FrameRenderBuffers[i].VertexBuffer.Handle = 0;
                    _mainWindowRenderBuffers.FrameRenderBuffers[i].VertexBufferSize = 0;
                    _mainWindowRenderBuffers.FrameRenderBuffers[i].VertexBufferMemory.Handle = 0;
                }
            }
            _mainWindowRenderBuffers.Index = (_mainWindowRenderBuffers.Index + 1) % _mainWindowRenderBuffers.Count;

            ref FrameRenderBuffer frameRenderBuffer = ref _mainWindowRenderBuffers.FrameRenderBuffers[_mainWindowRenderBuffers.Index];

            if (drawData.TotalVtxCount > 0)
            {
                // Create or resize the vertex/index buffers
                ulong vertex_size = (ulong)drawData.TotalVtxCount * (ulong)sizeof(ImDrawVert);
                ulong index_size = (ulong)drawData.TotalIdxCount * sizeof(ushort);
                if (frameRenderBuffer.VertexBuffer.Handle == default || frameRenderBuffer.VertexBufferSize < vertex_size)
                {
                    CreateOrResizeBuffer(ref frameRenderBuffer.VertexBuffer, ref frameRenderBuffer.VertexBufferMemory, ref frameRenderBuffer.VertexBufferSize, vertex_size, BufferUsageFlags.VertexBufferBit);
                }
                if (frameRenderBuffer.IndexBuffer.Handle == default || frameRenderBuffer.IndexBufferSize < index_size)
                {
                    CreateOrResizeBuffer(ref frameRenderBuffer.IndexBuffer, ref frameRenderBuffer.IndexBufferMemory, ref frameRenderBuffer.IndexBufferSize, index_size, BufferUsageFlags.IndexBufferBit);
                }

                // Upload vertex/index data into a single contiguous GPU buffer
                ImDrawVert* vtx_dst = null;
                ushort* idx_dst = null;
                if (_vkContext.Api.MapMemory(_device, frameRenderBuffer.VertexBufferMemory, 0, frameRenderBuffer.VertexBufferSize, 0, (void**)&vtx_dst) != Result.Success)
                {
                    throw new Exception($"Unable to map device memory");
                }
                if (_vkContext.Api.MapMemory(_device, frameRenderBuffer.IndexBufferMemory, 0, frameRenderBuffer.IndexBufferSize, 0, (void**)&idx_dst) != Result.Success)
                {
                    throw new Exception($"Unable to map device memory");
                }
                for (int n = 0; n < drawData.CmdListsCount; n++)
                {
                    ImDrawList* cmd_list = drawDataPtr.NativePtr->CmdLists[n];
                    Unsafe.CopyBlock(vtx_dst, cmd_list->VtxBuffer.Data.ToPointer(), (uint)cmd_list->VtxBuffer.Size * (uint)sizeof(ImDrawVert));
                    Unsafe.CopyBlock(idx_dst, cmd_list->IdxBuffer.Data.ToPointer(), (uint)cmd_list->IdxBuffer.Size * sizeof(ushort));
                    vtx_dst += cmd_list->VtxBuffer.Size;
                    idx_dst += cmd_list->IdxBuffer.Size;
                }

                Span<MappedMemoryRange> range = stackalloc MappedMemoryRange[2];
                range[0].SType = StructureType.MappedMemoryRange;
                range[0].Memory = frameRenderBuffer.VertexBufferMemory;
                range[0].Size = Vk.WholeSize;
                range[1].SType = StructureType.MappedMemoryRange;
                range[1].Memory = frameRenderBuffer.IndexBufferMemory;
                range[1].Size = Vk.WholeSize;
                if (_vkContext.Api.FlushMappedMemoryRanges(_device, 2, range) != Result.Success)
                {
                    throw new Exception($"Unable to flush memory to device");
                }
                _vkContext.Api.UnmapMemory(_device, frameRenderBuffer.VertexBufferMemory);
                _vkContext.Api.UnmapMemory(_device, frameRenderBuffer.IndexBufferMemory);
            }

            // Setup desired Vulkan state
            _vkContext.Api.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _pipeline);
            _vkContext.Api.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 0, 1, _descriptorSet, 0, null);

            // Bind Vertex And Index Buffer:
            if (drawData.TotalVtxCount > 0)
            {
                ReadOnlySpan<Buffer> vertex_buffers = stackalloc Buffer[] { frameRenderBuffer.VertexBuffer };
                ulong vertex_offset = 0;
                _vkContext.Api.CmdBindVertexBuffers(commandBuffer, 0, 1, vertex_buffers, (ulong*)Unsafe.AsPointer(ref vertex_offset));
                _vkContext.Api.CmdBindIndexBuffer(commandBuffer, frameRenderBuffer.IndexBuffer, 0, sizeof(ushort) == 2 ? IndexType.Uint16 : IndexType.Uint32);
            }

            // Setup viewport:
            Viewport viewport;
            viewport.X = 0;
            viewport.Y = 0;
            viewport.Width = fb_width;
            viewport.Height = fb_height;
            viewport.MinDepth = 0.0f;
            viewport.MaxDepth = 1.0f;
            _vkContext.Api.CmdSetViewport(commandBuffer, 0, 1, &viewport);

            // Setup scale and translation:
            // Our visible imgui space lies from draw_data.DisplayPps (top left) to draw_data.DisplayPos+data_data.DisplaySize (bottom right). DisplayPos is (0,0) for single viewport apps.
            Span<float> scale = stackalloc float[2];
            scale[0] = 2.0f / drawData.DisplaySize.X;
            scale[1] = 2.0f / drawData.DisplaySize.Y;
            Span<float> translate = stackalloc float[2];
            translate[0] = -1.0f - drawData.DisplayPos.X * scale[0];
            translate[1] = -1.0f - drawData.DisplayPos.Y * scale[1];
            _vkContext.Api.CmdPushConstants(commandBuffer, _pipelineLayout, ShaderStageFlags.VertexBit, sizeof(float) * 0, sizeof(float) * 2, scale);
            _vkContext.Api.CmdPushConstants(commandBuffer, _pipelineLayout, ShaderStageFlags.VertexBit, sizeof(float) * 2, sizeof(float) * 2, translate);

            // Will project scissor/clipping rectangles into framebuffer space
            Vector2 clipOff = drawData.DisplayPos;         // (0,0) unless using multi-viewports
            Vector2 clipScale = drawData.FramebufferScale; // (1,1) unless using retina display which are often (2,2)

            // Render command lists
            // (Because we merged all buffers into a single one, we maintain our own offset into them)
            int vertexOffset = 0;
            int indexOffset = 0;
            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                ImDrawList* cmd_list = drawDataPtr.NativePtr->CmdLists[n];
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

                    if (clipRect.X < fb_width && clipRect.Y < fb_height && clipRect.Z >= 0.0f && clipRect.W >= 0.0f)
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
                        _vkContext.Api.CmdSetScissor(commandBuffer, 0, 1, &scissor);

                        // Draw
                        _vkContext.Api.CmdDrawIndexed(commandBuffer, pcmd.ElemCount, 1, pcmd.IdxOffset + (uint)indexOffset, (int)pcmd.VtxOffset + vertexOffset, 0);
                    }
                }
                indexOffset += cmd_list->IdxBuffer.Size;
                vertexOffset += cmd_list->VtxBuffer.Size;
            }

            //_vkContext.Api.CmdEndRenderPass(commandBuffer);
        }

        unsafe void CreateOrResizeBuffer(ref Buffer buffer, ref DeviceMemory buffer_memory, ref ulong bufferSize, ulong newSize, BufferUsageFlags usage)
        {
            if (buffer.Handle != default)
            {
                _vkContext.Api.DeviceWaitIdle(_device);
                _vkContext.Api.DestroyBuffer(_device, buffer, default);
            }
            if (buffer_memory.Handle != default)
            {
                _vkContext.Api.FreeMemory(_device, buffer_memory, default);
            }

            ulong sizeAlignedVertexBuffer = ((newSize - 1) / _bufferMemoryAlignment + 1) * _bufferMemoryAlignment;
            var bufferInfo = new BufferCreateInfo();
            bufferInfo.SType = StructureType.BufferCreateInfo;
            bufferInfo.Size = sizeAlignedVertexBuffer;
            bufferInfo.Usage = usage;
            bufferInfo.SharingMode = SharingMode.Exclusive;
            if (_vkContext.Api.CreateBuffer(_device, in bufferInfo, default, out buffer) != Result.Success)
            {
                throw new Exception($"Unable to create a device buffer");
            }

            _vkContext.Api.GetBufferMemoryRequirements(_device, buffer, out var req);
            _bufferMemoryAlignment = _bufferMemoryAlignment > req.Alignment ? _bufferMemoryAlignment : req.Alignment;
            MemoryAllocateInfo allocInfo = new MemoryAllocateInfo();
            allocInfo.SType = StructureType.MemoryAllocateInfo;
            allocInfo.AllocationSize = req.Size;
            allocInfo.MemoryTypeIndex = GetMemoryTypeIndex(_vkContext.Api, MemoryPropertyFlags.HostVisibleBit, req.MemoryTypeBits);
            if (_vkContext.Api.AllocateMemory(_device, &allocInfo, default, out buffer_memory) != Result.Success)
            {
                throw new Exception($"Unable to allocate device memory");
            }

            if (_vkContext.Api.BindBufferMemory(_device, buffer, buffer_memory, 0) != Result.Success)
            {
                throw new Exception($"Unable to bind device memory");
            }
            bufferSize = req.Size;
        }


        private unsafe void InitPlatformInterface()
        {
            var platformIO = ImGui.GetPlatformIO();
            ImGuiViewportPtr mainViewport = platformIO.Viewports[0];
            mainViewport.PlatformHandle = _view.Handle;
            _mainViewportWindow = new ImGuiWindow(_vkContext, mainViewport, _view);
            // Set platform delegates

            _createWindow = Platform_CreateWindow;
            _destroyWindow = Platform_DestroyWindow;
            _getWindowPos = Platform_GetWindowPos;
            _showWindow = Platform_ShowWindow;
            _setWindowPos = Platform_SetWindowPos;
            _setWindowSize = Platform_SetWindowSize;
            _getWindowSize = Platform_GetWindowSize;
            _setWindowFocus = Platform_SetWindowFocus;
            _getWindowFocus = Platform_GetWindowFocus;
            _getWindowMinimized = Platform_GetWindowMinimized;
            _setWindowTitle = Platform_SetWindowTitle;
            _setWindowAlpha = Platform_SetWindowAlpha;

            platformIO.Platform_CreateWindow = Marshal.GetFunctionPointerForDelegate(_createWindow);
            platformIO.Platform_DestroyWindow = Marshal.GetFunctionPointerForDelegate(_destroyWindow);
            platformIO.Platform_ShowWindow = Marshal.GetFunctionPointerForDelegate(_showWindow);
            platformIO.Platform_SetWindowPos = Marshal.GetFunctionPointerForDelegate(_setWindowPos);
            platformIO.Platform_SetWindowSize = Marshal.GetFunctionPointerForDelegate(_setWindowSize);
            platformIO.Platform_SetWindowFocus = Marshal.GetFunctionPointerForDelegate(_setWindowFocus);
            platformIO.Platform_GetWindowFocus = Marshal.GetFunctionPointerForDelegate(_getWindowFocus);
            platformIO.Platform_GetWindowMinimized = Marshal.GetFunctionPointerForDelegate(_getWindowMinimized);
            platformIO.Platform_SetWindowTitle = Marshal.GetFunctionPointerForDelegate(_setWindowTitle);
            platformIO.Platform_SetWindowAlpha = Marshal.GetFunctionPointerForDelegate(_setWindowAlpha);
            ImGuiNative.ImGuiPlatformIO_Set_Platform_GetWindowPos(platformIO.NativePtr, Marshal.GetFunctionPointerForDelegate(_getWindowPos));
            ImGuiNative.ImGuiPlatformIO_Set_Platform_GetWindowSize(platformIO.NativePtr, Marshal.GetFunctionPointerForDelegate(_getWindowSize));


            // Set up monitor list
            SetupMonitors(platformIO);

        }
        private delegate void Platform_SetWindowAlphaDelegate(ImGuiViewportPtr viewport, float alpha);

        private unsafe void Platform_SetWindowAlpha(ImGuiViewportPtr viewport, float alpha)
        {
            ImGuiWindow window = (ImGuiWindow)GCHandle.FromIntPtr(viewport.PlatformUserData).Target;
            var surface = sdlApi.GetWindowSurface(SdlWindowing.GetHandle(window.Window));
            sdlApi.SetSurfaceAlphaMod(surface, (byte)alpha);
        }

        private unsafe void Platform_CreateWindow(ImGuiViewportPtr viewport)
        {
            ImGuiWindow window = new ImGuiWindow(_vkContext, viewport);
            foreach (var keyboard in window.Input.Keyboards)
            {
                keyboard.KeyChar += this.OnKeyChar;
            }
        }

        private unsafe void Platform_DestroyWindow(ImGuiViewportPtr viewport)
        {
            if (viewport.PlatformUserData != IntPtr.Zero)
            {
                ImGuiWindow window = (ImGuiWindow)GCHandle.FromIntPtr(viewport.PlatformUserData).Target;
                foreach (var item in window.Input.Keyboards)
                {
                    item.KeyChar -= this.OnKeyChar;
                }
                window.Dispose();

                viewport.PlatformUserData = IntPtr.Zero;
            }
        }

        private unsafe void Platform_ShowWindow(ImGuiViewportPtr viewport)
        {
            ImGuiWindow window = (ImGuiWindow)GCHandle.FromIntPtr(viewport.PlatformUserData).Target;
            window.Window.IsVisible = true;
        }

        private unsafe void Platform_SetWindowPos(ImGuiViewportPtr vp, Vector2 pos)
        {
            ImGuiWindow window = (ImGuiWindow)GCHandle.FromIntPtr(vp.PlatformUserData).Target;
            window.Window.Position = new Vector2D<int>((int)pos.X, (int)pos.Y);
        }

        private unsafe void Platform_GetWindowPos(ImGuiViewportPtr vp, Vector2* pos)
        {
            ImGuiWindow window = (ImGuiWindow)GCHandle.FromIntPtr((nint)vp.PlatformUserData).Target;
            *pos = new Vector2(window.Window.Position.X, window.Window.Position.Y);
        }

        private unsafe void Platform_SetWindowSize(ImGuiViewportPtr vp, Vector2 size)
        {
            ImGuiWindow window = (ImGuiWindow)GCHandle.FromIntPtr(vp.PlatformUserData).Target;
            window.Window.Size = new Vector2D<int>((int)size.X, (int)size.Y);
        }

        private unsafe void Platform_GetWindowSize(ImGuiViewportPtr vp, Vector2* outSize)
        {
            ImGuiWindow window = (ImGuiWindow)GCHandle.FromIntPtr(vp.PlatformUserData).Target;
            *outSize = new Vector2(window.Window.Size.X, window.Window.Size.Y);
        }

        private unsafe void Platform_SetWindowFocus(ImGuiViewportPtr vp)
        {
            ImGuiWindow window = (ImGuiWindow)GCHandle.FromIntPtr(vp.PlatformUserData).Target;
            sdlApi.SetWindowInputFocus(SdlWindowing.GetHandle(window.Window));
        }

        private unsafe byte Platform_GetWindowFocus(ImGuiViewportPtr vp)
        {
            ImGuiWindow window = (ImGuiWindow)GCHandle.FromIntPtr(vp.PlatformUserData).Target;
            return ((WindowFlags)sdlApi.GetWindowFlags(SdlWindowing.GetHandle(window.Window))).HasFlag(WindowFlags.InputFocus) ? (byte)1:(byte)0;
        }

        private unsafe byte Platform_GetWindowMinimized(ImGuiViewportPtr vp)
        {
            ImGuiWindow window = (ImGuiWindow)GCHandle.FromIntPtr(vp.PlatformUserData).Target;
            return ((WindowFlags)sdlApi.GetWindowFlags(SdlWindowing.GetHandle(window.Window))).HasFlag(WindowFlags.Minimized) ? (byte)1 : (byte)0;
        }

        private unsafe void Platform_SetWindowTitle(ImGuiViewportPtr vp, IntPtr title)
        {
            ImGuiWindow window = (ImGuiWindow)GCHandle.FromIntPtr(vp.PlatformUserData).Target;
            byte* titlePtr = (byte*)title;
            int count = 0;
            while (titlePtr[count] != 0)
            {
                count += 1;
            }
            sdlApi.SetWindowTitle(SdlWindowing.GetHandle(window.Window), titlePtr);
        }


        private unsafe void SetupMonitors(ImGuiPlatformIOPtr platformIO)
        {
            Marshal.FreeHGlobal(platformIO.NativePtr->Monitors.Data);
            int numMonitors = sdlApi.GetNumVideoDisplays();
            IntPtr data = Marshal.AllocHGlobal(Unsafe.SizeOf<ImGuiPlatformMonitor>() * numMonitors);
            platformIO.NativePtr->Monitors = new ImVector(numMonitors, numMonitors, data);
            for (int i = 0; i < numMonitors; i++)
            {
                Rectangle<int> r;
                sdlApi.GetDisplayBounds(i, &r);
                ImGuiPlatformMonitorPtr monitor = platformIO.Monitors[i];
                monitor.DpiScale = 1f;
                monitor.MainPos = new Vector2(r.Origin.X, r.Origin.Y);
                monitor.MainSize = new Vector2(r.Size.X, r.Size.Y);
                monitor.WorkPos = new Vector2(r.Origin.X, r.Origin.Y);
                monitor.WorkSize = new Vector2(r.Size.X, r.Size.Y);
            }
        }
        /// <summary>
        /// Frees all graphics resources used by the renderer.
        /// </summary>
        public unsafe void Dispose()
        {
            _view.Resize -= WindowResized;
            _keyboard.KeyChar -= OnKeyChar;

            for (uint n = 0; n < _mainWindowRenderBuffers.Count; n++)
            {
                _vkContext.Api.DestroyBuffer(_device, _mainWindowRenderBuffers.FrameRenderBuffers[n].VertexBuffer, default);
                _vkContext.Api.FreeMemory(_device, _mainWindowRenderBuffers.FrameRenderBuffers[n].VertexBufferMemory, default);
                _vkContext.Api.DestroyBuffer(_device, _mainWindowRenderBuffers.FrameRenderBuffers[n].IndexBuffer, default);
                _vkContext.Api.FreeMemory(_device, _mainWindowRenderBuffers.FrameRenderBuffers[n].IndexBufferMemory, default);
            }

            _vkContext.Api.DestroyShaderModule(_device, _shaderModuleVert, default);
            _vkContext.Api.DestroyShaderModule(_device, _shaderModuleFrag, default);
            _vkContext.Api.DestroyImageView(_device, _fontView, default);
            _vkContext.Api.DestroyImage(_device, _fontImage, default);
            _vkContext.Api.FreeMemory(_device, _fontMemory, default);
            _vkContext.Api.DestroySampler(_device, _fontSampler, default);
            _vkContext.Api.DestroyDescriptorSetLayout(_device, _descriptorSetLayout, default);
            _vkContext.Api.DestroyPipelineLayout(_device, _pipelineLayout, default);
            _vkContext.Api.DestroyPipeline(_device, _pipeline, default);
            _vkContext.Api.DestroyDescriptorPool(_vkContext.Api.CurrentDevice.Value, _descriptorPool, default);
            _vkContext.Api.DestroyRenderPass(_vkContext.Api.CurrentDevice.Value, _renderPass, default);

            ImGui.DestroyContext();
        }

        struct FrameRenderBuffer
        {
            public DeviceMemory VertexBufferMemory;
            public DeviceMemory IndexBufferMemory;
            public ulong VertexBufferSize;
            public ulong IndexBufferSize;
            public Buffer VertexBuffer;
            public Buffer IndexBuffer;
        };

        unsafe struct WindowRenderBuffers
        {
            public uint Index;
            public uint Count;
            public FrameRenderBuffer* FrameRenderBuffers;
        };
    }
}

