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
        private VkSwapchain _swapchain;
        private InputAttachmentBinding _attachmentBinding;
        private readonly BindingManager _bindingManager;
        private VkPipeline? _pipeline;
        private readonly GraphicsEngine _graphicsEngine;

        public VkImageView[] ColorAttachments { get; private set; }
        public VkImageView DepthAttachment { get; private set; }
        public VkSampler Sampler { get; private set; }
        public Texture[] ColorTextures { get; private set; }
        public DescriptorSet[] LightingDescriptorSets { get; private set; }

        private static readonly Format[] ColorAttachmentFormats =
        [
            Format.R16G16B16A16Sfloat,    // Position (RGBA16F) - 8 bytes
            Format.R8G8Unorm,               // Normal (2 bytes)
            Format.R8G8B8A8Srgb           // Albedo + Specular (SRGB) - 4 bytes
        ];

        public Material Material { get; private set; }
        public VkSampler[] Samplers { get; private set; }

        public GBuffer(VulkanContext context, VkSwapchain swapchain, GraphicsEngine graphicsEngine, BindingManager bindingManager)
        {
            _context = context;
            _swapchain = swapchain;
            _graphicsEngine = graphicsEngine;
            _bindingManager = bindingManager;

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
                ColorAttachments[0],  // Position
                ColorAttachments[1],  // Normal
                ColorAttachments[2]   // Albedo
            );
            _bindingManager.AllocateAndUpdateDescriptorSet(_attachmentBinding, _pipeline.Layout);
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
            _context.SubmitSingleTimeCommand(cmd =>
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
        private VkImageView CreateColorAttachment(Format format)
        {
            var image = VkImage.Create(
                _context,
                _swapchain.Extent.Width,
                _swapchain.Extent.Height,
                format,
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
                    Samplers[i], // Use appropriate sampler for each texture
                null);
            }
            foreach (var texture in ColorTextures)
            {
                if (texture.Image.CurrentLayout != ImageLayout.ShaderReadOnlyOptimal)
                {
                    throw new InvalidOperationException(
                        $"Texture layout {texture.Image.CurrentLayout} is invalid for descriptor set"
                    );
                }
            }
        }


        public void Recreate(VkSwapchain swapchain)
        {
            _context.Device.GraphicsQueue.WaitIdle();

            // Cleanup old resources
            foreach (var attachment in ColorAttachments) attachment?.Dispose();
            foreach (var texture in ColorTextures) texture?.Dispose();
            DepthAttachment?.Dispose();

            // Update swapchain reference
            _swapchain = swapchain;

            CreateAttachments();
            CreateTextures();
            CreateLightingDescriptorSets(_pipeline);
        }

    }
}

