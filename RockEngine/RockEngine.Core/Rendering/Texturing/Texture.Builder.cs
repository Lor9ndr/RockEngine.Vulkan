using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Texturing
{
public partial class Texture
    {
        public class Builder
        {
            private readonly VulkanContext _context;
            private Extent2D _size;
            private Format _format = Format.R8G8B8A8Unorm;
            private ImageUsageFlags _usage = ImageUsageFlags.SampledBit;
            private ImageAspectFlags _aspectMask = ImageAspectFlags.ColorBit;
            private SamplerCreateInfo? _samplerCreateInfo;
            private uint _mipLevels = 1;
            private bool _generateMipmaps;

            public Builder(VulkanContext context)
            {
                _context = context ?? throw new ArgumentNullException(nameof(context));
            }

            public Builder SetSize(Extent2D size)
            {
                _size = size;
                return this;
            }

            public Builder SetFormat(Format format)
            {
                _format = format;
                return this;
            }

            public Builder SetUsage(ImageUsageFlags usage)
            {
                _usage = usage;
                return this;
            }

            public Builder SetAspectMask(ImageAspectFlags aspectMask)
            {
                _aspectMask = aspectMask;
                return this;
            }

            public Builder SetSamplerSettings(SamplerCreateInfo samplerInfo)
            {
                samplerInfo.SType = StructureType.SamplerCreateInfo;
                _samplerCreateInfo = samplerInfo;
                return this;
            }

            public Builder ForRenderTarget()
            {
                _usage |= ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit;
                _aspectMask = ImageAspectFlags.ColorBit;
                _mipLevels = 1; // Typically don't need mips for render targets
                return this;
            }

            public Builder WithMipmaps(bool generate = true)
            {
                _generateMipmaps = generate;
                if (generate)
                {
                    _mipLevels = CalculateMipLevels(_size.Width, _size.Height);
                    _usage |= ImageUsageFlags.TransferSrcBit;
                }
                return this;
            }

            public Texture Build()
            {
                ValidateParameters();

                var image = CreateVulkanImage();
                var imageView = CreateImageView(image);
                var sampler = CreateSampler();

                if (_generateMipmaps)
                {
                    throw new NotImplementedException();
                    //GenerateMipmaps();
                }

                return new Texture(_context, image, imageView, sampler);
            }
            private void ValidateParameters()
            {
                if (_size.Width == 0 || _size.Height == 0)
                    throw new InvalidOperationException("Texture size must be specified");

                if (_format.IsBlockCompressed())
                {
                    var blockSize = _format.GetBlockSize();
                    if (_size.Width % blockSize.Width != 0 || _size.Height % blockSize.Height != 0)
                    {
                        throw new InvalidOperationException(
                            $"Compressed texture dimensions must be multiples of {blockSize}");
                    }
                }
            }
            private VkImage CreateVulkanImage()
            {
                var imageInfo = new ImageCreateInfo
                {
                    SType = StructureType.ImageCreateInfo,
                    ImageType = ImageType.Type2D,
                    Format = _format,
                    Extent = new Extent3D(_size.Width, _size.Height, 1),
                    MipLevels = _mipLevels,
                    ArrayLayers = 1,
                    Samples = SampleCountFlags.Count1Bit,
                    Tiling = ImageTiling.Optimal,
                    Usage = _usage,
                    SharingMode = SharingMode.Exclusive,
                    InitialLayout = ImageLayout.Undefined
                };

                return VkImage.Create(_context, imageInfo, MemoryPropertyFlags.DeviceLocalBit, _aspectMask);
            }

            private VkImageView CreateImageView(VkImage image)
            {
                var viewInfo = new ImageViewCreateInfo
                {
                    SType = StructureType.ImageViewCreateInfo,
                    Image = image,
                    ViewType = ImageViewType.Type2D,
                    Format = _format,
                    SubresourceRange = new ImageSubresourceRange
                    {
                        AspectMask = _aspectMask,
                        BaseMipLevel = 0,
                        LevelCount = _mipLevels,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    }
                };

                return VkImageView.Create(_context, image, viewInfo);
            }

            private VkSampler CreateSampler()
            {
                var samplerInfo = _samplerCreateInfo ?? new SamplerCreateInfo
                {
                    SType = StructureType.SamplerCreateInfo,
                    MagFilter = Filter.Linear,
                    MinFilter = Filter.Linear,
                    AddressModeU = SamplerAddressMode.Repeat,
                    AddressModeV = SamplerAddressMode.Repeat,
                    AddressModeW = SamplerAddressMode.Repeat,
                    AnisotropyEnable = Vk.True,
                    MaxAnisotropy = 16,
                    BorderColor = BorderColor.IntOpaqueBlack,
                    UnnormalizedCoordinates = Vk.False,
                    CompareEnable = Vk.False,
                    CompareOp = CompareOp.Always,
                    MipmapMode = SamplerMipmapMode.Linear,
                    MipLodBias = 0.0f,
                    MinLod = 0.0f,
                    MaxLod = (float)_mipLevels
                };

                return _context.SamplerCache.GetSampler(samplerInfo);
            }
        }
    }

}
