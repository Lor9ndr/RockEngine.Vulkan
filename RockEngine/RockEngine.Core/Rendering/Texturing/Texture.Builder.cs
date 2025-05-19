using Assimp;

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
            private bool _isCubeMap;
            private uint _arrayLayers = 1;
            private SharingMode _sharingMode = SharingMode.Exclusive;
            private uint[] _queueFamilyIndices = Array.Empty<uint>();
            private bool _waitCompute;

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
            public Builder WaitCompute()
            {
                _waitCompute = true;
                return this;
            }

            public Builder SetSamplerSettings(SamplerCreateInfo samplerInfo)
            {
                samplerInfo.SType = StructureType.SamplerCreateInfo;
                _samplerCreateInfo = samplerInfo;
                return this;
            }
            public Builder SetSharingMode(SharingMode sharingMode)
            {
                _sharingMode = sharingMode;
                return this;
            }

            public Builder SetQueueFamilyIndices(uint[] queueFamilyIndices)
            {
                _queueFamilyIndices = queueFamilyIndices;
                return this;
            }

            public Builder SetCubemap(bool isCubeMap)
            {
                _isCubeMap = isCubeMap;
                if (_isCubeMap)
                {
                    _arrayLayers = 6;
                }
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
                if (_waitCompute)
                {
                    var semaphore = VkSemaphore.Create(_context);
                    _context.SubmitComputeContext.AddWaitSemaphore(semaphore, PipelineStageFlags.ComputeShaderBit);
                    _context.SubmitContext.AddSignalSemaphore(semaphore);
                }
                if (_generateMipmaps)
                {
                    var batch = _context.SubmitContext.CreateBatch();
                    image.GenerateMipmaps(batch.CommandBuffer);
                    batch.Submit();
                    _context.SubmitContext.Flush();

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
                unsafe
                {
                    fixed(uint* pQueueFamilyIndices = _queueFamilyIndices)
                    {
                        var imageInfo = new ImageCreateInfo
                        {
                            SType = StructureType.ImageCreateInfo,
                            ImageType = ImageType.Type2D,
                            Format = _format,
                            Extent = new Extent3D(_size.Width, _size.Height, 1),
                            MipLevels = _mipLevels,
                            ArrayLayers = _arrayLayers,
                            Samples = SampleCountFlags.Count1Bit,
                            Tiling = ImageTiling.Optimal,
                            Usage = _usage,
                            SharingMode = _sharingMode,
                            QueueFamilyIndexCount = (uint)_queueFamilyIndices.Length,
                            PQueueFamilyIndices = pQueueFamilyIndices,
                            InitialLayout = ImageLayout.Undefined,
                            Flags = _isCubeMap ? ImageCreateFlags.CreateCubeCompatibleBit : ImageCreateFlags.None
                        };

                        return VkImage.Create(_context, imageInfo, MemoryPropertyFlags.DeviceLocalBit, _aspectMask);
                    }
                }
               
            }

            private VkImageView CreateImageView(VkImage image)
            {
                return image.GetOrCreateView(_aspectMask, 0, _mipLevels, 0, _arrayLayers);

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
