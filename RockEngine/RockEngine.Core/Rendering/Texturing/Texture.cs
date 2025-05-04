using RockEngine.Core.Helpers;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using SkiaSharp;

using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Texturing
{
    /// <summary>
    /// Represents a Vulkan texture resource, managing image, image view, and sampler lifecycle.
    /// Handles 2D textures, cubemaps, and provides texture creation utilities.
    /// </summary>
    public partial class Texture : IDisposable
    {
        #region Fields and Properties

        // Vulkan context for resource management
        protected readonly VulkanContext _context;

        // Vulkan image resources
        protected VkImage _image;
        protected VkImageView _imageView;
        protected VkSampler _sampler;

        // Resource management flags
        private bool _disposed;
        private static Texture? _emptyTexture;
        private uint _loadedMipLevels;

        /// <summary>Image view for texture sampling</summary>
        public VkImageView ImageView => _imageView;

        /// <summary>Texture sampler for filtering and addressing</summary>
        public VkSampler Sampler => _sampler;

        /// <summary>Vulkan image resource</summary>
        public VkImage Image => _image;

        /// <summary>Number of loaded mipmap levels</summary>
        public uint LoadedMipLevels { get => _loadedMipLevels; protected set => _loadedMipLevels = value; }

        /// <summary>Total available mipmap levels</summary>
        public uint TotalMipLevels => _image.MipLevels;

        /// <summary>Texture width in pixels</summary>
        public uint Width => _image.Extent.Width;

        /// <summary>Texture height in pixels</summary>
        public uint Height => _image.Extent.Height;

        /// <summary>Unique identifier for the texture</summary>
        public Guid ID { get; } = Guid.NewGuid();

        /// <summary>Original source file path (if loaded from disk)</summary>
        public string? SourcePath { get; }

        /// <summary>Event triggered when texture data is updated</summary>
        public event Action<Texture>? OnTextureUpdated;

        #endregion


        #region Constructor and Core Methods

        /// <summary>
        /// Creates a texture from existing Vulkan resources
        /// </summary>
        /// <param name="context">Vulkan context</param>
        /// <param name="image">Pre-created Vulkan image</param>
        /// <param name="imageView">Image view for the texture</param>
        /// <param name="sampler">Texture sampler</param>
        /// <param name="sourcePath">Optional source file path</param>
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

        /// <summary>
        /// Factory method to create a texture from a file
        /// </summary>
        /// <param name="context">Vulkan context</param>
        /// <param name="filePath">Path to texture file</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>New texture instance</returns>
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
                var batch = context.SubmitContext.CreateBatch();
                image.GenerateMipmaps(batch.CommandBuffer);
                batch.Submit();
                await context.SubmitContext.FlushAsync();
            }

            var sampler = CreateSampler(context, mipLevels);
            return new Texture(context, image, imageView, sampler, filePath);
        }

        protected void NotifyTextureUpdated()
        {
            OnTextureUpdated?.Invoke(this);
        }

        /// <summary>
        /// Creates a texture from raw pixel data
        /// </summary>
        /// <param name="context">Vulkan context</param>
        /// <param name="width">Texture width</param>
        /// <param name="height">Texture height</param>
        /// <param name="format">Pixel format</param>
        /// <param name="data">Raw pixel data pointer</param>
        /// <returns>New texture instance</returns>
        public static unsafe Texture Create(VulkanContext context, int width, int height, Format format, nint data)
        {
            var vkImage = CreateVulkanImage(context, (uint)width, (uint)height, format);
            var imageView = CreateImageView(context, vkImage, format);

            CopyImageDataFromPointer(context, vkImage, data.ToPointer(), (uint)width, (uint)height, format);

            var sampler = CreateSampler(context, vkImage.MipLevels);
            return new Texture(context, vkImage, imageView, sampler, null);
        }
        #endregion

        #region Vulkan Resource Creation
        /// <summary>
        /// Creates a Vulkan image with specified parameters
        /// </summary>
        /// <param name="context">Vulkan context</param>
        /// <param name="width">Image width</param>
        /// <param name="height">Image height</param>
        /// <param name="format">Pixel format</param>
        /// <param name="usage">Image usage flags</param>
        /// <param name="mipLevels">Number of mip levels</param>
        /// <param name="arrayLayers">Number of array layers</param>
        /// <param name="flags">Image creation flags</param>
        /// <returns>Created Vulkan image</returns>
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
        /// <summary>
        /// Creates an image view for a Vulkan image
        /// </summary>
        /// <param name="context">Vulkan context</param>
        /// <param name="image">Source image</param>
        /// <param name="format">Pixel format</param>
        /// <returns>Created image view</returns>
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
        /// <summary>
        /// Creates a texture sampler with default or specified parameters
        /// </summary>
        /// <param name="context">Vulkan context</param>
        /// <param name="mipLevels">Number of mip levels</param>
        /// <returns>Created sampler</returns>
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

        /// <summary>
        /// Generates mipmaps for the texture using Vulkan commands
        /// </summary>
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

        /// <summary>
        /// Calculates the maximum number of mip levels for given dimensions
        /// </summary>
        /// <param name="width">Base width</param>
        /// <param name="height">Base height</param>
        /// <returns>Number of mip levels</returns>
        private static uint CalculateMipLevels(uint width, uint height)
        {
            return (uint)Math.Floor(Math.Log(Math.Max(width, height), 2)) + 1;
        }

        /// <summary>
        /// Maps SkiaSharp color type to Vulkan format
        /// </summary>
        /// <param name="colorType">SkiaSharp color type</param>
        /// <param name="context">Vulkan context</param>
        /// <returns>Best matching Vulkan format</returns>
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
        #endregion

        #region Data Transfer

        /// <summary>
        /// Copies pixel data from SkiaSharp bitmap to Vulkan image
        /// </summary>

        private static unsafe void CopyImageData(VulkanContext context, SKBitmap skBitmap, VkImage vkImage, bool keepTransferLayout = false)
        {
            CopyImageDataFromPointer(context, vkImage, skBitmap.GetPixels().ToPointer(), (uint)skBitmap.Width, (uint)skBitmap.Height, GetVulkanFormat(skBitmap.ColorType, context), keepTransferLayout);
        }


        /// <summary>
        /// Copies raw pixel data to Vulkan image
        /// </summary>
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
        #endregion

        #region Special Textures

        /// <summary>
        /// Gets or creates a default pink 1x1 texture
        /// </summary>

        public static Texture GetEmptyTexture(VulkanContext context)
        {
            _emptyTexture ??= CreateEmptyTexture(context);
            return _emptyTexture;
        }
        /// <summary>
        /// Creates a default pink 1x1 texture
        /// </summary>

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
        #endregion

        #region Cubemap Handling

        /// <summary>
        /// Creates a cubemap texture from six face images
        /// </summary>
        /// <param name="context">Vulkan context</param>
        /// <param name="facePaths">
        /// Array of 6 face paths in order:
        /// [ +X, -X, +Y, -Y, +Z, -Z ]
        /// </param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Created cubemap texture</returns>
        /// <exception cref="ArgumentException">Invalid number of faces</exception>
        /// <exception cref="InvalidOperationException">Face size/format mismatch</exception>
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

            // 1. Transition entire image to TRANSFER_DST_OPTIMAL
            image.TransitionImageLayout(
                uploadBatch.CommandBuffer,
                ImageLayout.TransferDstOptimal,
                baseMipLevel: 0,
                levelCount: 1,
                baseArrayLayer: 0,
                layerCount: 6);

            // 2. Copy data to each face's base mip level
            for (int i = 0; i < 6; i++)
            {
                byte[] pixelData = faceBitmaps[i].Bytes;
                ulong faceSize = (ulong)(width * height * format.GetBytesPerPixel());

                if (!context.SubmitContext.StagingManager.TryStage(pixelData, out ulong bufferOffset, out ulong stagedSize))
                {
                    throw new InvalidOperationException("Staging buffer overflow");
                }

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

                VulkanContext.Vk.CmdCopyBufferToImage(
                    uploadBatch.CommandBuffer,
                    context.SubmitContext.StagingManager.StagingBuffer,
                    image,
                    ImageLayout.TransferDstOptimal,
                    1,
                    in copyRegion);
            }

            // 3. Transition base mip to TRANSFER_SRC_OPTIMAL for mipmap generation
            image.TransitionImageLayout(
                uploadBatch.CommandBuffer,
                ImageLayout.TransferSrcOptimal,
                baseMipLevel: 0,
                levelCount: 1,
                baseArrayLayer: 0,
                layerCount: 6);

            // 4. Generate mipmaps for each face
            image.GenerateMipmaps(uploadBatch.CommandBuffer);

           /* // 5. Transition all mip levels to SHADER_READ_ONLY_OPTIMAL
            image.TransitionImageLayout(
                uploadBatch.CommandBuffer,
                ImageLayout.ShaderReadOnlyOptimal,
                baseMipLevel: 0,
                levelCount: mipLevels,
                baseArrayLayer: 0,
                layerCount: 6);*/

            // Submit all commands
            uploadBatch.Submit();
            await context.SubmitContext.FlushAsync();

            // Create associated resources
            var imageView = CreateCubeMapImageView(context, image, format);
            var sampler = CreateCubeMapSampler(context, mipLevels);

            return new Texture(context, image, imageView, sampler, null);
        }


        /// <summary>
        /// Creates a cubemap-specific sampler
        /// </summary>
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


        /// <summary>
        /// Creates a cubemap image view
        /// </summary>
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

            // Verify image is in correct layout
            if (image.GetMipLayout(0, 0) != ImageLayout.ShaderReadOnlyOptimal)
            {
                throw new InvalidOperationException("Cubemap image must be in SHADER_READ_ONLY layout before view creation");
            }

            return VkImageView.Create(context, image, viewInfo);
        }
        #endregion

        #region Disposal and Cleanup

        /// <summary>
        /// Releases Vulkan resources
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _imageView.Dispose();
                _image.Dispose();
                _disposed = true;
            }
        }

        #endregion
    }
}
