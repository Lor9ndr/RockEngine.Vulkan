using RockEngine.Core.Helpers;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using SkiaSharp;

using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Texturing
{
    public class Texture : IDisposable
    {
        protected readonly VulkanContext _context;
        protected VkImage _image;
        protected VkImageView _imageView;
        protected VkSampler _sampler;
        private bool _disposed;
        private static Texture? _emptyTexture;
        private uint _loadedMipLevels;

        public VkImageView ImageView => _imageView;
        public VkSampler Sampler => _sampler;
        public VkImage Image => _image;

        public uint LoadedMipLevels { get => _loadedMipLevels; protected set => _loadedMipLevels = value; }
        public uint TotalMipLevels => _image.MipLevels;

        public uint Width => _image.Extent.Width;
        public uint Height => _image.Extent.Height;

        public Guid ID { get; } = Guid.NewGuid();
        public string? SourcePath { get; }

        public event Action<Texture>? OnTextureUpdated;

        public Texture(VulkanContext context, VkImage image, VkImageView imageView, VkSampler sampler, string? sourcePath = null)
        {
            _context = context;
            _image = image;
            _imageView = imageView;
            _sampler = sampler;
            SourcePath = sourcePath;
            LoadedMipLevels = 1;
            Image.OnImageResized += (img) => NotifyTextureUpdated();
        }

        public static async Task<Texture> CreateAsync(VulkanContext context, string filePath, CancellationToken cancellationToken = default)
        {
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
            using var skBitmap = SKBitmap.Decode(bytes);

            var width = (uint)skBitmap.Width;
            var height = (uint)skBitmap.Height;
            var format = GetVulkanFormat(skBitmap.Info.ColorType, context);
            uint mipLevels = CalculateMipLevels(width, height);

            var image = CreateVulkanImage(context, width, height, format,
                ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
                mipLevels);

            var imageView = CreateImageView(context, image, format);

            // Copy initial data and leave in TRANSFER_DST_OPTIMAL if generating mipmaps
            CopyImageData(context, skBitmap, image, mipLevels > 1);

            if (mipLevels > 1)
            {
                context.SubmitSingleTimeCommand(cmd =>
                {
                    image.GenerateMipmaps(cmd);
                });
            }

            var sampler = CreateSampler(context, mipLevels);
            return new Texture(context, image, imageView, sampler, filePath);
        }

        protected void NotifyTextureUpdated()
        {
            OnTextureUpdated?.Invoke(this);
        }


        public static unsafe Texture Create(VulkanContext context, int width, int height, Format format, nint data)
        {
            var vkImage = CreateVulkanImage(context, (uint)width, (uint)height, format);
            var imageView = CreateImageView(context, vkImage, format);

            CopyImageDataFromPointer(context, vkImage, data.ToPointer(), (uint)width, (uint)height, format);

            var sampler = CreateSampler(context, vkImage.MipLevels);
            return new Texture(context, vkImage, imageView, sampler, null);
        }

        private static VkImage CreateVulkanImage(
         VulkanContext context,
         uint width,
         uint height,
         Format format,
         ImageUsageFlags usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
         uint mipLevels = 1,
         uint arrayLayers = 1,
         ImageCreateFlags flags = ImageCreateFlags.None)
        {
            // Validate compressed texture dimensions
            if (format.IsBlockCompressed())
            {
                var blockSize = format.GetBlockSize();
                if (width % blockSize.Width != 0 || height % blockSize.Height != 0)
                {
                    throw new InvalidOperationException(
                        $"Compressed texture dimensions must be multiples of {blockSize}");
                }
            }

            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = format,
                Extent = new Extent3D(width, height, 1),
                MipLevels = mipLevels,
                ArrayLayers = arrayLayers,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined,
                Flags = flags
            };

            return VkImage.Create(context, imageInfo, MemoryPropertyFlags.DeviceLocalBit, ImageAspectFlags.ColorBit);
        }

        public static VkImageView CreateImageView(VulkanContext context, VkImage image, Format format)
        {
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = image,
                ViewType = ImageViewType.Type2D,
                Format = format,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = image.MipLevels,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            return VkImageView.Create(context, image, viewInfo);
        }

        protected static VkSampler CreateSampler(VulkanContext context, uint mipLevels)
        {
            var samplerCreateInfo = new SamplerCreateInfo
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
                MaxLod = mipLevels
            };

            return context.SamplerCache.GetSampler(samplerCreateInfo);
        }
        public static async Task<Texture> CreateBaseImageAsync(VulkanContext context, string texturePath)
        {
            using var bitmap = SKBitmap.Decode(texturePath);
            uint width = (uint)bitmap.Width;
            uint height = (uint)bitmap.Height;
            uint mipLevels = CalculateMipLevels(width, height);
            var format = GetVulkanFormat(bitmap.ColorType, context);

            var image = VkImage.Create(
                context,
                 new ImageCreateInfo()
                 {
                     Extent = new Extent3D(width, height, 1),
                     Format = format,
                     Tiling = ImageTiling.Optimal,
                     Usage = ImageUsageFlags.SampledBit |
                         ImageUsageFlags.TransferDstBit |
                         ImageUsageFlags.TransferSrcBit,
                     SType = StructureType.ImageCreateInfo,
                     Samples = SampleCountFlags.Count1Bit,
                     ImageType = ImageType.Type2D,
                     ArrayLayers = 1,
                     MipLevels = mipLevels,
                     InitialLayout = ImageLayout.Undefined,
                 },
                MemoryPropertyFlags.DeviceLocalBit, ImageAspectFlags.ColorBit);

            // Копирование данных и генерация мипмапов
            var batch = context.SubmitContext.CreateBatch();
            var staging = await VkBuffer.CreateAndCopyToStagingBuffer<byte>(context,bitmap.GetPixelSpan().ToArray(),(ulong)bitmap.ByteCount);

            image.TransitionImageLayout(batch.CommandBuffer, ImageLayout.TransferDstOptimal);

            var copy = new BufferImageCopy
            {
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                },
                ImageExtent = new Extent3D(width, height, 1)
            };

            VulkanContext.Vk.CmdCopyBufferToImage(batch.CommandBuffer, staging, image,
                ImageLayout.TransferDstOptimal, 1, in copy);

            image.GenerateMipmaps(batch.CommandBuffer);
            batch.Submit([staging]);
            await context.SubmitContext.FlushAsync();

            return new Texture(context, image,
                image.CreateView(ImageAspectFlags.ColorBit, 0, mipLevels),
                CreateSampler(context, mipLevels), texturePath);

        }

        private static uint CalculateMipLevels(uint width, uint height)
        {
            return (uint)Math.Floor(Math.Log(Math.Max(width, height), 2)) + 1;
        }

        private static Format GetVulkanFormat(SKColorType colorType, VulkanContext context)
        {
            var features = context.Device.PhysicalDevice.GetPhysicalDeviceFeatures();

            // Check for BC compression support
            if (features.TextureCompressionBC && colorType == SKColorType.Rgba8888)
            {
                return Format.BC3UnormBlock;
            }

            // Fallback to existing formats
            return colorType switch
            {
                SKColorType.Rgba8888 => Format.R8G8B8A8Unorm,
                SKColorType.Bgra8888 => Format.B8G8R8A8Unorm,
                SKColorType.Gray8 => Format.R8Unorm,
                SKColorType.RgbaF32 => Format.R32G32B32A32Sfloat,
                _ => throw new NotSupportedException($"Unsupported color type: {colorType}")
            };
        }


        private static unsafe void CopyImageData(VulkanContext context, SKBitmap skBitmap, VkImage vkImage, bool keepTransferLayout = false)
        {
            CopyImageDataFromPointer(context, vkImage, skBitmap.GetPixels().ToPointer(), (uint)skBitmap.Width, (uint)skBitmap.Height, GetVulkanFormat(skBitmap.ColorType, context), keepTransferLayout);
        }


        private static unsafe void CopyImageDataFromPointer(
             VulkanContext context,
             VkImage vkImage,
             void* data,
             uint width,
             uint height,
             Format format,
             bool keepTransferLayout = false)
        {
            if (data == null)
            {
                // Handle the case where data is null
                // For example, you might want to initialize the image with a default color or skip the copy
                return;
            }
            var imageSize = (ulong)(width * height * format.GetBytesPerPixel());

            // Create a staging buffer
            using var stagingBuffer = VkBuffer.CreateAndCopyToStagingBuffer(context, data, imageSize);

            VkCommandPool? cmdPool = null;
            cmdPool = VkCommandPool.Create(context, CommandPoolCreateFlags.TransientBit, context.Device.QueueFamilyIndices.GraphicsFamily.Value);
            var commandBuffer = cmdPool.AllocateCommandBuffer();
            commandBuffer.BeginSingleTimeCommand();

            using var fence = VkFence.CreateNotSignaled(context);
            var fenceNative = fence.VkObjectNative;
            // Transition the Vulkan image layout to TRANSFER_DST_OPTIMAL
            vkImage.TransitionImageLayout(commandBuffer,  ImageLayout.TransferDstOptimal);

            // Copy the data from the staging buffer to the Vulkan image
            CopyBufferToImage(commandBuffer, stagingBuffer, vkImage, width, height);

            // Transition the Vulkan image layout to SHADER_READ_ONLY_OPTIMAL
            if (!keepTransferLayout)
            {
                vkImage.TransitionImageLayout(commandBuffer,  ImageLayout.ShaderReadOnlyOptimal);
            }
            commandBuffer.End();

            var realBuffer = commandBuffer.VkObjectNative;
            var submitInfo = new SubmitInfo();
            submitInfo.SType = StructureType.SubmitInfo;
            submitInfo.CommandBufferCount = 1;
            submitInfo.PCommandBuffers = &realBuffer;
            context.Device.GraphicsQueue.Submit(in submitInfo, fence);
            VulkanContext.Vk.WaitForFences(context.Device, 1, in fenceNative, true, ulong.MaxValue);
            cmdPool?.Dispose();
        }


        private static unsafe void CopyBufferToImage(VkCommandBuffer commandBuffer, VkBuffer stagingBuffer, VkImage image, uint width, uint height)
        {
            var region = new BufferImageCopy
            {
                BufferOffset = 0,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                ImageOffset = new Offset3D { X = 0, Y = 0, Z = 0 },
                ImageExtent = new Extent3D { Width = width, Height = height, Depth = 1 }
            };

            VulkanContext.Vk.CmdCopyBufferToImage(commandBuffer, stagingBuffer, image, ImageLayout.TransferDstOptimal, 1, &region);
        }



        public static Texture GetEmptyTexture(VulkanContext context)
        {
            _emptyTexture ??= CreateEmptyTexture(context);
            return _emptyTexture;
        }

        private static Texture CreateEmptyTexture(VulkanContext context)
        {
            // Create a 1x1 pink texture
            using var surface = SKSurface.Create(new SKImageInfo(1, 1, SKColorType.Rgba8888));
            surface.Canvas.Clear(SKColors.Pink);
            using var image = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);
            return LoadFromSKImage(context, bitmap);
        }

        public static Texture LoadFromSKImage(VulkanContext context, SKBitmap skImage)
        {
            var width = (uint)skImage.Width;
            var height = (uint)skImage.Height;
            var format = GetVulkanFormat(skImage.Info.ColorType, context);
            var vkImage = CreateVulkanImage(context, width, height, format);
            var imageView = CreateImageView(context, vkImage, format);

            unsafe
            {
                // Copy image data from SkiaSharp to Vulkan image
                CopyImageDataFromPointer(context, vkImage, skImage.GetPixels().ToPointer(), width, height, format);
            }

            // Create a sampler
            var samplerCreateInfo = new SamplerCreateInfo
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
                MaxLod = 0.0f
            };
           
            var sampler = context.SamplerCache.GetSampler(samplerCreateInfo);
            skImage.Dispose();
            return new Texture(context, vkImage, imageView, sampler, null);
        }
        public static async Task<Texture> CreateCubeMapAsync(VulkanContext context, string[] facePaths, CancellationToken cancellationToken = default)
        {
            if (facePaths.Length != 6)
                throw new ArgumentException("Cube map requires exactly 6 face paths.");

            // Load all face images
            var faceBitmaps = new SKBitmap[6];
            for (int i = 0; i < 6; i++)
            {
                var bytes = await File.ReadAllBytesAsync(facePaths[i], cancellationToken);
                faceBitmaps[i] = SKBitmap.Decode(bytes);
                if (faceBitmaps[i].Width != faceBitmaps[0].Width || faceBitmaps[i].Height != faceBitmaps[0].Height)
                    throw new InvalidOperationException("Cube map faces must have uniform dimensions.");
                if (faceBitmaps[i].ColorType != faceBitmaps[0].ColorType)
                    throw new InvalidOperationException("Cube map faces must have the same color format.");
            }

            // Extract common properties
            uint width = (uint)faceBitmaps[0].Width;
            uint height = (uint)faceBitmaps[0].Height;
            var format = GetVulkanFormat(faceBitmaps[0].ColorType, context);
            uint mipLevels = CalculateMipLevels(width, height);

            // Create cube-compatible image
            var image = CreateVulkanImage(
                context,
                width,
                height,
                format,
                ImageUsageFlags.TransferDstBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.SampledBit,
                mipLevels,
                arrayLayers: 6,
                flags: ImageCreateFlags.CreateCubeCompatibleBit);

            var uploadBatch = context.SubmitContext.CreateBatch();

            // 1. Transition image to transfer destination layout
            image.TransitionImageLayout(uploadBatch.CommandBuffer, ImageLayout.TransferDstOptimal, levelCount:6);

            // 2. Stage all face data and schedule copies
            for (int i = 0; i < 6; i++)
            {
                byte[] pixelData = faceBitmaps[i].Bytes;
                ulong faceSize = (ulong)(width * height * format.GetBytesPerPixel());

                // Stage data using the staging manager
                if (!context.SubmitContext.StagingManager.TryStage(pixelData, out ulong bufferOffset, out ulong stagedSize))
                {
                    throw new InvalidOperationException("Staging buffer overflow");
                }

                // Configure buffer-to-image copy
                var copyRegion = new BufferImageCopy
                {
                    BufferOffset = bufferOffset,
                    ImageSubresource = new ImageSubresourceLayers
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        MipLevel = 0,
                        BaseArrayLayer = (uint)i,
                        LayerCount = 1
                    },
                    ImageExtent = new Extent3D(width, height, 1)
                };

                // Record copy command
                VulkanContext.Vk.CmdCopyBufferToImage(
                    uploadBatch.CommandBuffer,
                    context.SubmitContext.StagingManager.StagingBuffer,
                    image,
                    ImageLayout.TransferDstOptimal,
                    1,
                    in copyRegion);
            }

            // 3. Generate mipmaps
            image.GenerateMipmaps(uploadBatch.CommandBuffer);

/*            // 4. Transition to final layout
            image.TransitionImageLayout(uploadBatch.CommandBuffer, ImageLayout.ShaderReadOnlyOptimal);*/

            // Submit all commands as a single batch
            uploadBatch.Submit();

            // Create associated resources
            var imageView = CreateCubeMapImageView(context, image, format);
            var sampler = CreateCubeMapSampler(context, mipLevels);

            return new Texture(context, image, imageView, sampler, null);
        }
        private static VkSampler CreateCubeMapSampler(VulkanContext context, uint mipLevels)
        {
            var samplerCreateInfo = new SamplerCreateInfo
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Linear,
                MinFilter = Filter.Linear,
                AddressModeU = SamplerAddressMode.ClampToEdge,
                AddressModeV = SamplerAddressMode.ClampToEdge,
                AddressModeW = SamplerAddressMode.ClampToEdge,
                AnisotropyEnable = Vk.True,
                MaxAnisotropy = 16,
                BorderColor = BorderColor.IntOpaqueBlack,
                UnnormalizedCoordinates = Vk.False,
                CompareEnable = Vk.False,
                CompareOp = CompareOp.Always,
                MipmapMode = SamplerMipmapMode.Linear,
                MipLodBias = 0.0f,
                MinLod = 0.0f,
                MaxLod = mipLevels
            };

            return context.SamplerCache.GetSampler(in samplerCreateInfo);
        }
        private static VkImageView CreateCubeMapImageView(VulkanContext context, VkImage image, Format format)
        {
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = image,
                ViewType = ImageViewType.TypeCube,
                Format = format,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = image.MipLevels,
                    BaseArrayLayer = 0,
                    LayerCount = 6
                }
            };

            return VkImageView.Create(context, image, viewInfo);
        }



        public void Dispose()
        {
            if (!_disposed)
            {
                _imageView.Dispose();
                _image.Dispose();
                _disposed = true;
            }
        }

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

    public static class FormatExtensions
    {
        public static bool IsBlockCompressed(this Format format)
        {
            return format.ToString().StartsWith("BC");
        }

        public static (int Width, int Height) GetBlockSize(this Format format)
        {
            return format.ToString() switch
            {
                string s when s.StartsWith("BC") => (4, 4),
                _ => (1, 1)
            };
        }
    }

}
