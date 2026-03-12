using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.RenderTargets
{
    public class ShadowRenderTarget : RenderTarget
    {
        private readonly VulkanContext _context;
        private readonly Light _light;

        public VkImage Image { get; private set; }
        public VkImageView ImageView { get; private set; }
        public VkSampler Sampler { get; private set; }
        public Light Light => _light;
        public readonly LightType LightType;
        //  Proper layer count calculation for CSM
        public uint LayerCount
        {
            get
            {
                if (_light.Type == LightType.Point)
                    return 6u; // Cube map faces
                else if (_light.Type == LightType.Directional && _light.CascadeCount > 1)
                    return (uint)_light.CascadeCount; // CSM cascades
                else
                    return 1u; // Single layer for spot/directional without CSM
            }
        }

        public ShadowRenderTarget(VulkanContext context, Light light)
            :base(context, new Extent2D(light.ShadowMapSize, light.ShadowMapSize), Format.D32Sfloat, ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferSrcBit)
        {
            _context = context;
            _light = light;
            LightType = light.Type;
        }

        public override void Initialize(RckRenderPass renderPass)
        {
            RenderPass = renderPass;
            CreateImage();
            CreateImageView();
            CreateSampler();
            CreateFramebuffers();

            Viewport = new Viewport(0, 0, _light.ShadowMapSize, _light.ShadowMapSize, 0, 1);
            Scissor = new Rect2D(new Offset2D(), new Extent2D(_light.ShadowMapSize, _light.ShadowMapSize));

            ClearValues = new ClearValue[]
            {
                new ClearValue { DepthStencil = new ClearDepthStencilValue(1.0f, 0) }
            };
        }

        private void CreateImage()
        {
            var createInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = Format,
                Extent = new Extent3D(_light.ShadowMapSize, _light.ShadowMapSize, 1),
                MipLevels = 1,
                ArrayLayers = LayerCount,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferSrcBit,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined,
                // Only use cube flag for point lights
                Flags = _light.Type == LightType.Point ? ImageCreateFlags.CreateCubeCompatibleBit : ImageCreateFlags.None
            };

            Image = VkImage.Create(_context, createInfo, MemoryPropertyFlags.DeviceLocalBit, ImageAspectFlags.DepthBit);
            Image.LabelObject("ShadowRenderTarget");

            // Transition to optimal layout for depth attachment
            var batch = _context.GraphicsSubmitContext.CreateBatch();
            Image.TransitionImageLayout(
                batch,
                ImageLayout.Undefined,
                ImageLayout.DepthStencilAttachmentOptimal,
                baseMipLevel: 0,
                levelCount: 1,
                baseArrayLayer: 0,
                layerCount: LayerCount
            );
            batch.Submit();
        }

        private void CreateImageView()
        {
            ImageView = Image.GetOrCreateView(
                   ImageAspectFlags.DepthBit,
                   baseArrayLayer: 0,
                   layerCount: LayerCount
               );
        }

        private void CreateSampler()
        {
            var createInfo = new SamplerCreateInfo
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Linear,
                MinFilter = Filter.Linear,
                AddressModeU = SamplerAddressMode.ClampToEdge,
                AddressModeV = SamplerAddressMode.ClampToEdge,
                AddressModeW = SamplerAddressMode.ClampToEdge,
                AnisotropyEnable = false,
                MaxAnisotropy = 1,
                BorderColor = BorderColor.FloatOpaqueWhite,
                UnnormalizedCoordinates = false,
                CompareEnable = true,
                CompareOp = CompareOp.Less,
                MipmapMode = SamplerMipmapMode.Linear,
                MipLodBias = 0,
                MinLod = 0,
                MaxLod = 1
            };

            Sampler = VkSampler.Create(_context, createInfo);
        }

      
        public override void PrepareForRender(UploadBatch batch)
        {
        }

        public override void TransitionToRead(UploadBatch batch)
        {
        }

        protected override void CreateFramebuffers()
        {

            // Create framebuffer with correct layer count
            Framebuffers = [VkFrameBuffer.Create(
                _context,
                RenderPass.RenderPass,
                [ImageView],
                _light.ShadowMapSize,
                _light.ShadowMapSize,
                LayerCount
            )];
        }
        protected override void DisposeResources()
        {
            foreach (var framebuffer in Framebuffers)
            {
                framebuffer?.Dispose();
            }
            Sampler?.Dispose();
            ImageView?.Dispose();
            Image?.Dispose();
        }
    }
}