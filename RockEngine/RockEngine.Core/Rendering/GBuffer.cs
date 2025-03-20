using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering
{
    public class GBuffer
    {
        private readonly RenderingContext _context;
        private VkSwapchain _swapchain;
        private TextureBinding _binding;
        private BindingManager _currentBindingManager;
        private VkPipelineLayout _currentLayout;
        private readonly GraphicsEngine _graphicsEngine;

        public VkImageView[] ColorAttachments { get; private set; }
        public VkImageView DepthAttachment { get; private set; }
        public VkFrameBuffer FrameBuffer { get; private set; }
        public VkRenderPass RenderPass { get; private set; }
        public VkSampler Sampler { get; private set; }
        public Texture[] ColorTextures { get; private set; }
        public DescriptorSet[] LightingDescriptorSets { get; private set; }

        public GBuffer(RenderingContext context, VkSwapchain swapchain, GraphicsEngine graphicsEngine)
        {
            _context = context;
            _swapchain = swapchain;
            _graphicsEngine = graphicsEngine;
            _swapchain.OnSwapchainRecreate += Recreate;

            Initialize();
        }

        private void Initialize()
        {
            CreateAttachments();
            CreateSampler();
            CreateTextures();
            CreateRenderPass();
            CreateFramebuffer();
        }
        private void CreateAttachments()
        {
            ColorAttachments = new VkImageView[3];
            for (int i = 0; i < ColorAttachments.Length; i++)
            {
                ColorAttachments[i] = CreateColorAttachment();
            }
            DepthAttachment = CreateDepthAttachment();
            _graphicsEngine.SubmitSingleTimeCommand(cmd =>
            {
                foreach (var attachment in ColorAttachments)
                {
                    attachment.Image.TransitionImageLayout(
                        cmd,
                        Format.R16G16B16A16Sfloat,
                        ImageLayout.ShaderReadOnlyOptimal);

                }
            });
        }
        private VkImageView CreateColorAttachment()
        {
            var image = VkImage.Create(
                                _context,
                                _swapchain.Extent.Width,
                                _swapchain.Extent.Height,
                                Format.R16G16B16A16Sfloat,
                                ImageTiling.Optimal,
                                ImageUsageFlags.ColorAttachmentBit |
                                ImageUsageFlags.InputAttachmentBit |
                                ImageUsageFlags.SampledBit,
                                MemoryPropertyFlags.DeviceLocalBit);

            return image.CreateView(ImageAspectFlags.ColorBit);
        }

        private VkImageView CreateDepthAttachment()
        {
            var image = VkImage.Create(_context,
                                            _swapchain.Extent.Width,
                                            _swapchain.Extent.Height,
                                            _swapchain.DepthFormat,
                                            ImageTiling.Optimal,
                                            ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.InputAttachmentBit,
                                            MemoryPropertyFlags.DeviceLocalBit);

            return image.CreateView(ImageAspectFlags.DepthBit);
        }
        private void CreateSampler()
        {
            var samplerInfo = new SamplerCreateInfo
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Linear,
                MinFilter = Filter.Linear,
                AddressModeU = SamplerAddressMode.ClampToEdge,
                AddressModeV = SamplerAddressMode.ClampToEdge,
                AddressModeW = SamplerAddressMode.ClampToEdge
            };
            Sampler = VkSampler.Create(_context, samplerInfo);
        }
        private void CreateTextures()
        {
            ColorTextures = new Texture[ColorAttachments.Length];
            for (int i = 0; i < ColorAttachments.Length; i++)
            {
                ColorTextures[i] = new Texture(
                    _context,
                    ColorAttachments[i].Image,
                    ColorAttachments[i],
                    Sampler
                );
            }
        }

        private void Recreate(VkSwapchain swapchain)
        {
            _context.Device.GraphicsQueue.WaitIdle();

            // Cleanup old resources
            foreach (var attachment in ColorAttachments) attachment?.Dispose();
            foreach (var texture in ColorTextures) texture?.Dispose();
            DepthAttachment?.Dispose();
            FrameBuffer?.Dispose();

            // Update swapchain reference
            _swapchain = swapchain;

            CreateAttachments();
            CreateTextures();
            CreateFramebuffer();
            _context.Device.GraphicsQueue.WaitIdle();
            RecreateDescriptors(); 
        }
        public void RecreateDescriptors()
        {
            if (_currentBindingManager == null || _currentLayout == null) return;

            // Create new descriptors with updated textures
            CreateLightingDescriptorSets(_currentBindingManager, _currentLayout);
        }
        private unsafe void CreateRenderPass()
        {
            var colorAttachmentDescriptions = new AttachmentDescription[ColorAttachments.Length];
            for (int i = 0; i < colorAttachmentDescriptions.Length; i++)
            {
                colorAttachmentDescriptions[i] = new AttachmentDescription
                {
                    Format = Format.R16G16B16A16Sfloat,
                    Samples = SampleCountFlags.Count1Bit,
                    LoadOp = AttachmentLoadOp.Clear,
                    StoreOp = AttachmentStoreOp.Store,
                    StencilLoadOp = AttachmentLoadOp.DontCare,
                    StencilStoreOp = AttachmentStoreOp.DontCare,
                    InitialLayout = ImageLayout.Undefined,
                    FinalLayout = ImageLayout.ShaderReadOnlyOptimal
                };
            }

            var depthAttachmentDescription = new AttachmentDescription
            {
                Format = _swapchain.DepthFormat,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.DontCare,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
            };

            var colorAttachmentReferences = stackalloc AttachmentReference[ColorAttachments.Length];
            for (int i = 0; i < ColorAttachments.Length; i++)
            {
                colorAttachmentReferences[i] = new AttachmentReference { Attachment = (uint)i, Layout = ImageLayout.ColorAttachmentOptimal };
            }

            var depthAttachmentReference = new AttachmentReference { Attachment = (uint)ColorAttachments.Length, Layout = ImageLayout.DepthStencilAttachmentOptimal };

            var subpassDescription = new SubpassDescription
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = (uint)ColorAttachments.Length,
                PColorAttachments = colorAttachmentReferences,
                PDepthStencilAttachment = &depthAttachmentReference
            };

            var subpassDependency = new SubpassDependency
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
                SrcAccessMask = AccessFlags.None,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
                DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit
            };

            var attachments = colorAttachmentDescriptions.Concat(new[] { depthAttachmentDescription }).ToArray();
            RenderPass =  VkRenderPass.Create(_context, new RenderPassCreateInfo
            {
                SType = StructureType.RenderPassCreateInfo,
                AttachmentCount = (uint)attachments.Length,
                PAttachments = (AttachmentDescription*)attachments.AsMemory().Pin().Pointer,
                SubpassCount = 1,
                PSubpasses = &subpassDescription,
                DependencyCount = 1,
                PDependencies = &subpassDependency
            });
        }

        private unsafe void CreateFramebuffer()
        {
            var attachments = ColorAttachments.Concat([DepthAttachment]).ToArray();

            fixed(ImageView* pAttachments = attachments.Select(s => s.VkObjectNative).ToArray())
            {
                FrameBuffer =  VkFrameBuffer.Create(_context, new FramebufferCreateInfo
                {
                    SType = StructureType.FramebufferCreateInfo,
                    RenderPass = RenderPass,
                    AttachmentCount = (uint)attachments.Length,
                    PAttachments = pAttachments,
                    Width = _swapchain.Extent.Width,
                    Height = _swapchain.Extent.Height,
                    Layers = 1
                }, attachments);
            }
        }

        public void CreateLightingDescriptorSets(BindingManager bindingManager, VkPipelineLayout layout)
        {
            _currentBindingManager = bindingManager;
            _currentLayout = layout;


            _binding = new TextureBinding(0, 0, ColorTextures);
            bindingManager.AllocateAndUpdateDescriptorSet(_binding, layout);
        }

        public void BindResources(BindingManager bindingManager, VkPipelineLayout layout, VkCommandBuffer commandBuffer)
        {
            bindingManager.BindBinding(_binding, layout, commandBuffer, 0);
        }

        public unsafe void BeginGeometryPass(VkCommandBuffer commandBuffer, Extent2D extent)
        {
            int clearValuesLength = ColorAttachments.Length + 1;
            ClearValue* clearValues = stackalloc ClearValue[clearValuesLength];

            // Initialize color clear values
            for (int i = 0; i < ColorAttachments.Length; i++)
            {
                clearValues[i] = new ClearValue
                {
                    Color = new ClearColorValue(0.0f, 0.0f, 0.0f, 1.0f)
                };
            }

            // Initialize depth clear value
            clearValues[ColorAttachments.Length] = new ClearValue
            {
                DepthStencil = new ClearDepthStencilValue(1.0f, 0)
            };

            var beginInfo = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = RenderPass,
                Framebuffer = FrameBuffer,
                ClearValueCount = (uint)clearValuesLength,
                PClearValues = clearValues,
                RenderArea = new Rect2D { Extent = extent }
            };

            commandBuffer.BeginRenderPass(in beginInfo, SubpassContents.Inline);
        }
    }
}
