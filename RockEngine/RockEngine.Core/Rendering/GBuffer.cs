using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering
{
    public class GBuffer
    {
        private readonly VulkanContext _context;
        private InputAttachmentBinding _attachmentBinding;
        private readonly Format _depthFormat;
        private VkPipeline? _pipeline;
        private Extent2D _size;

        public VkImageView[] ColorAttachments { get; private set; }
        public VkImageView DepthAttachment { get; private set; }
        public VkSampler Sampler { get; private set; }
        public Texture[] ColorTextures { get; private set; }
        public DescriptorSet[] LightingDescriptorSets { get; private set; }

        public static readonly Format[] ColorAttachmentFormats =
        [
            Format.R16G16B16A16Sfloat,    // Position (RGBA16F) - 8 bytes
            Format.R8G8Unorm,               // Normal (2 bytes)
            Format.R8G8B8A8Srgb           // Albedo + Specular (SRGB) - 4 bytes
        ];

        public Material Material { get; private set; }
        public VkSampler[] Samplers { get; private set; }

        public GBuffer(VulkanContext context, Extent2D size, Format depthFormat)
        {
            _context = context;
            _size = size;
            _depthFormat = depthFormat;

            Initialize();
        }

        private void Initialize()
        {
            CreateAttachments();
            CreateSamplers();
            CreateTextures();

        }
        public void CreateLightingDescriptorSets(VkPipeline pipeline)
        {
            if (_pipeline != pipeline)
            {
                _pipeline = pipeline;
                Material = new Material(_pipeline, ColorTextures!.ToList());
            }

            Material.Bindings.Remove(_attachmentBinding);
            _attachmentBinding = new InputAttachmentBinding(
                setLocation: 2,
                bindingLocation: 0,
                ColorAttachments  // Position + Normal + Albedo
            );
            Material.Bindings.Add(_attachmentBinding);
        }

        private void CreateAttachments()
        {
            ColorAttachments = new VkImageView[3];
            for (int i = 0; i < ColorAttachments.Length; i++)
            {
                ColorAttachments[i] = CreateColorAttachment(ColorAttachmentFormats[i]);
            }
            DepthAttachment = CreateDepthAttachment();
          
        }
        private VkImageView CreateColorAttachment(Format format)
        {
            var image = VkImage.Create(
                _context,
                _size.Width,
                _size.Height,
                format,
                ImageTiling.Optimal,
                ImageUsageFlags.ColorAttachmentBit |
                ImageUsageFlags.InputAttachmentBit |
                ImageUsageFlags.SampledBit,
                MemoryPropertyFlags.DeviceLocalBit, aspectFlags: ImageAspectFlags.ColorBit);

            return image.CreateView(ImageAspectFlags.ColorBit);
        }

        private VkImageView CreateDepthAttachment()
        {
            var image = VkImage.Create(_context,
                _size.Width,
                _size.Height,
                _depthFormat,
                ImageTiling.Optimal,
                ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit,
                MemoryPropertyFlags.DeviceLocalBit,
                initialLayout: ImageLayout.Undefined,
                aspectFlags: ImageAspectFlags.DepthBit);

            return image.CreateView(ImageAspectFlags.DepthBit);
        }
        private void CreateSamplers()
        {
            // Create separate samplers for different texture types
            var positionSampler = CreateSampler(Filter.Nearest);  // Position benefits from nearest
            var normalSampler = CreateSampler(Filter.Linear);     // Normals need smooth interpolation
            var albedoSampler = CreateSampler(Filter.Linear);     // Albedo with sRGB handling

            Samplers = new[] { positionSampler, normalSampler, albedoSampler };
        }

        private VkSampler CreateSampler(Filter filter)
        {
            var samplerInfo = new SamplerCreateInfo
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = filter,
                MinFilter = filter,
                AddressModeU = SamplerAddressMode.ClampToEdge,
                AddressModeV = SamplerAddressMode.ClampToEdge,
                AddressModeW = SamplerAddressMode.ClampToEdge,
                MipmapMode = SamplerMipmapMode.Linear,
                MinLod = 0.0f,
                MaxLod = 0.0f,  // Disable mipmapping
                BorderColor = BorderColor.FloatOpaqueBlack,
                UnnormalizedCoordinates = false
            };

            if (filter == Filter.Linear)
            {
                samplerInfo.AnisotropyEnable = true;
                samplerInfo.MaxAnisotropy = _context.Device.PhysicalDevice.Properties.Limits.MaxSamplerAnisotropy;
            }

            return VkSampler.Create(_context, samplerInfo);
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
                    Samplers[i], 
                null);
            }
        }


        public void Recreate(Extent2D size)
        {
            _context.Device.GraphicsQueue.WaitIdle();

            // Cleanup old resources
            foreach (var attachment in ColorAttachments) attachment?.Dispose();
            foreach (var texture in ColorTextures) texture?.Dispose();
            DepthAttachment?.Dispose();
            _size = size;

            CreateAttachments();
            CreateTextures();
            CreateLightingDescriptorSets(_pipeline);
        }
    }
}

