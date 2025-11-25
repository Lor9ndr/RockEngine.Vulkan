using RockEngine.Core.Helpers;
using RockEngine.Vulkan;

using Silk.NET.Maths;
using Silk.NET.Vulkan;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using SkiaSharp;

using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Texturing
{
    public sealed class Texture2D : Texture
    {
        private static Texture2D? _emptyTexture;
        private static Texture2D? _emptyNormalTexture;
        private static Texture2D? _emptyMRATexture;
        private static Texture2D? _emptyDepthTexture;
        private static Texture2D? _emptyWhiteTexture;
        private static Texture2D? _emptyBlackTexture;
        private static Texture2D? _emptyBlueTexture;

        public uint Width => _image.Extent.Width;
        public uint Height => _image.Extent.Height;

        public Texture2D(VulkanContext context, VkImage image,
                        VkSampler sampler, string? sourcePath = null)
            : base(context, image, sampler, sourcePath) { }

        // Factory method for creating empty textures with parameters
        public static Texture2D CreateEmpty(VulkanContext context,
            uint width = 1,
            uint height = 1,
            Format format = Format.R8G8B8A8Unorm,
            ImageUsageFlags usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
            ImageLayout initialLayout = ImageLayout.ShaderReadOnlyOptimal,
            Filter filter = Filter.Linear,
            SamplerAddressMode addressMode = SamplerAddressMode.Repeat,
            string? name = null)
        {
            var image = CreateVulkanImage(context, width, height, format, 1, usage);
            var sampler = CreateSampler(context, 1, filter, addressMode);

            // Transition to desired layout if not already there
            if (initialLayout != ImageLayout.Undefined)
            {
                var batch = context.GraphicsSubmitContext.CreateBatch();
                image.TransitionImageLayout(
                    batch.CommandBuffer,
                    initialLayout,
                    baseMipLevel: 0,
                    levelCount: 1
                );
                batch.Submit();
            }

            if (!string.IsNullOrEmpty(name))
            {
                image.LabelObject(name);
            }

            return new Texture2D(context, image, sampler);
        }

        // Factory method for creating empty depth textures
        public static Texture2D CreateEmptyDepth(VulkanContext context,
            uint width = 1,
            uint height = 1,
            Format format = Format.D32Sfloat,
            ImageUsageFlags usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit,
            string? name = null)
        {
            var image = CreateVulkanImage(context, width, height, format, 1, usage, ImageAspectFlags.DepthBit);

            // Create depth-specific sampler
            var samplerCreateInfo = new SamplerCreateInfo
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

            var sampler = context.SamplerCache.GetSampler(samplerCreateInfo);

            if (!string.IsNullOrEmpty(name))
            {
                image.LabelObject(name);
            }

            return new Texture2D(context, image, sampler);
        }

        // Factory method for creating empty array textures (for shadow maps)
        public static Texture2D CreateEmptyArray(VulkanContext context,
            uint width,
            uint height,
            uint arrayLayers,
            Format format = Format.R8G8B8A8Unorm,
            ImageUsageFlags usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
            Filter filter = Filter.Linear,
            SamplerAddressMode addressMode = SamplerAddressMode.ClampToEdge,
            string? name = null)
        {
            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = format,
                Extent = new Extent3D(width, height, 1),
                MipLevels = 1,
                ArrayLayers = arrayLayers,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined
            };

            var image = VkImage.Create(context, imageInfo, MemoryPropertyFlags.DeviceLocalBit, ImageAspectFlags.DepthBit);

            // Transition to shader read-only optimal
            var batch = context.GraphicsSubmitContext.CreateBatch();
            image.TransitionImageLayout(
                batch.CommandBuffer,
                ImageLayout.ShaderReadOnlyOptimal,
                baseMipLevel: 0,
                levelCount: 1
            );
            batch.Submit();

            var sampler = CreateSampler(context, 1, filter, addressMode);

            if (!string.IsNullOrEmpty(name))
            {
                image.LabelObject(name);
            }

            return new Texture2D(context, image, sampler);
        }

        // Factory method for creating empty cube textures
        public static Texture2D CreateEmptyCube(VulkanContext context,
            uint size,
            Format format = Format.R8G8B8A8Unorm,
            ImageUsageFlags usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
            Filter filter = Filter.Linear,
            SamplerAddressMode addressMode = SamplerAddressMode.ClampToEdge,
            string? name = null)
        {
            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = format,
                Extent = new Extent3D(size, size, 1),
                MipLevels = 1,
                ArrayLayers = 6, // Cube has 6 faces
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined,
                Flags = ImageCreateFlags.CreateCubeCompatibleBit
            };

            var image = VkImage.Create(context, imageInfo, MemoryPropertyFlags.DeviceLocalBit, ImageAspectFlags.DepthBit);

            // Transition to shader read-only optimal
            var batch = context.GraphicsSubmitContext.CreateBatch();
            image.TransitionImageLayout(
                batch.CommandBuffer,
                ImageLayout.ShaderReadOnlyOptimal,
                baseMipLevel: 0,
                levelCount: 1
            );
            batch.Submit();

            var sampler = CreateSampler(context, 1, filter, addressMode);

            if (!string.IsNullOrEmpty(name))
            {
                image.LabelObject(name);
            }

            return new Texture2D(context, image, sampler);
        }

        // Predefined empty textures
        public static Texture2D GetEmptyTexture(VulkanContext context)
        {
            _emptyTexture ??= CreateColorTexture(context, new Vector4D<byte>(128, 128, 128, 255), "Empty_Default");
            return _emptyTexture;
        }

        public static Texture2D GetEmptyWhiteTexture(VulkanContext context)
        {
            _emptyWhiteTexture ??= CreateColorTexture(context, new Vector4D<byte>(255, 255, 255, 255), "Empty_White");
            return _emptyWhiteTexture;
        }

        public static Texture2D GetEmptyBlackTexture(VulkanContext context)
        {
            _emptyBlackTexture ??= CreateColorTexture(context, new Vector4D<byte>(0, 0, 0, 255), "Empty_Black");
            return _emptyBlackTexture;
        }

        public static Texture2D GetEmptyNormalTexture(VulkanContext context)
        {
            _emptyNormalTexture ??= CreateColorTexture(context, new Vector4D<byte>(128, 128, 255, 255), "Empty_Normal");
            return _emptyNormalTexture;
        }

        public static Texture2D GetEmptyMRATexture(VulkanContext context)
        {
            _emptyMRATexture ??= CreateColorTexture(context, new Vector4D<byte>(255, 1, 1, 255), "Empty_MRA");
            return _emptyMRATexture;
        }

        public static Texture2D GetEmptyDepthTexture(VulkanContext context)
        {
            _emptyDepthTexture ??= CreateEmptyDepth(context, 1, 1, Format.D32Sfloat, ImageUsageFlags.SampledBit, "Empty_Depth");
            return _emptyDepthTexture;
        }

        // Enhanced color texture creation
        public static Texture2D CreateColorTexture(VulkanContext context, Vector4D<byte> color, string? name = null)
        {
            using var surface = SKSurface.Create(new SKImageInfo(1, 1, SKColorType.Rgba8888));
            surface.Canvas.Clear(new SKColor(color.X, color.Y, color.Z, color.W));
            using var image = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);
            return LoadFromSKImage(context, bitmap, name);
        }

        // Shadow map specific creation methods
        public static Texture2D CreateShadowMap(VulkanContext context,
            uint size,
            Format format = Format.D32Sfloat,
            uint arrayLayers = 1,
            string? name = null)
        {
            var usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit;

            if (arrayLayers > 1)
            {
                return CreateEmptyArray(context, size, size, arrayLayers, format, usage, Filter.Linear, SamplerAddressMode.ClampToEdge, name);
            }
            else
            {
                return CreateEmptyDepth(context, size, size, format, usage, name);
            }
        }

        public static Texture2D CreatePointShadowMap(VulkanContext context,
            uint size,
            Format format = Format.D32Sfloat,
            string? name = null)
        {
            var usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit;
            return CreateEmptyCube(context, size, format, usage, Filter.Linear, SamplerAddressMode.ClampToEdge, name);
        }

        // Existing methods remain the same, but updated to use new helpers
        public static async Task<Texture2D> CreateAsync(VulkanContext context, byte[] data, string name, CancellationToken cancellationToken = default)
        {
            return await CreateFromBytesAsync(context, data, name, cancellationToken);
        }

        public static async Task<Texture2D> CreateAsync(VulkanContext context, Stream stream, string name, CancellationToken cancellationToken = default)
        {
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);
            var bytes = memoryStream.ToArray();

            return await CreateFromBytesAsync(context, bytes, name, cancellationToken);
        }

        public static async Task<Texture2D> CreateAsync(VulkanContext context, string filePath, CancellationToken cancellationToken = default)
        {
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            return await CreateFromBytesAsync(context, bytes, filePath, cancellationToken);
        }

        private static async Task<Texture2D> CreateFromBytesAsync(VulkanContext context,
            byte[] bytes, string name, CancellationToken cancellationToken = default)
        {
            var extension = Path.GetExtension(name)?.ToLower();
            SKBitmap skBitmap;

            if (extension == ".tga")
            {
                using Image<Rgba32> imageSharpImage = SixLabors.ImageSharp.Image.Load<Rgba32>(bytes);
                skBitmap = new SKBitmap(imageSharpImage.Width, imageSharpImage.Height);

                imageSharpImage.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        Span<Rgba32> row = accessor.GetRowSpan(y);
                        Span<byte> srcBytes = MemoryMarshal.AsBytes(row);
                        Span<byte> dstBytes = skBitmap.GetPixelSpan().Slice(y * skBitmap.RowBytes, srcBytes.Length);
                        srcBytes.CopyTo(dstBytes);
                    }
                });
            }
            else
            {
                skBitmap = SKBitmap.Decode(bytes);
            }

            var texture = await CreateFromSkBitmapAsync(context, skBitmap, name, cancellationToken);
            skBitmap.Dispose();
            return texture;
        }

        public static async Task<Texture2D> CreateFromSkBitmapAsync(VulkanContext context,
            SKBitmap skBitmap, string name, CancellationToken cancellationToken = default)
        {
            var width = (uint)skBitmap.Width;
            var height = (uint)skBitmap.Height;
            var format = GetVulkanFormat(skBitmap.Info.ColorType, context);
            uint totalMipLevels = CalculateMipLevels(width, height);

            var image = CreateVulkanImage(context, width, height, format, totalMipLevels);

            var transferComplete = VkSemaphore.Create(context);
            var graphicsComplete = VkSemaphore.Create(context);

            // Upload base level
            var transferBatch = context.TransferSubmitContext.CreateBatch();
            CopyImageData(context, transferBatch, skBitmap, image);
            transferBatch.AddSignalSemaphore(transferComplete);

            await context.TransferSubmitContext.FlushSingle(transferBatch, VkFence.CreateNotSignaled(context));

            // Generate all mip levels on GPU
            var graphicsBatch = context.GraphicsSubmitContext.CreateBatch();
            graphicsBatch.AddWaitSemaphore(transferComplete, PipelineStageFlags.TransferBit);

            if (totalMipLevels > 1)
            {
                image.GenerateMipmaps(graphicsBatch.CommandBuffer);
            }
            else
            {
                image.TransitionImageLayout(
                    graphicsBatch.CommandBuffer,
                    ImageLayout.ShaderReadOnlyOptimal,
                    baseMipLevel: 0,
                    levelCount: 1
                );
            }

            graphicsBatch.AddSignalSemaphore(graphicsComplete);
            graphicsBatch.Submit();

            image.LabelObject(name);
            var sampler = CreateSampler(context, totalMipLevels);

            return new Texture2D(context, image, sampler, name);
        }

        // Updated helper methods
        private static VkImage CreateVulkanImage(
            VulkanContext context,
            uint width,
            uint height,
            Format format,
            uint mipLevels = 1,
            ImageUsageFlags usageFlags = ImageUsageFlags.TransferSrcBit |
                        ImageUsageFlags.TransferDstBit |
                        ImageUsageFlags.SampledBit,
            ImageAspectFlags aspectFlags = ImageAspectFlags.ColorBit)
        {
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
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = usageFlags,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined
            };

            return VkImage.Create(context, imageInfo, MemoryPropertyFlags.DeviceLocalBit, aspectFlags);
        }

        private static VkSampler CreateSampler(VulkanContext context, uint mipLevels,
            Filter filter = Filter.Linear,
            SamplerAddressMode addressMode = SamplerAddressMode.Repeat)
        {
            var samplerCreateInfo = new SamplerCreateInfo
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = filter,
                MinFilter = filter,
                AddressModeU = addressMode,
                AddressModeV = addressMode,
                AddressModeW = addressMode,
                AnisotropyEnable = true,
                MaxAnisotropy = 16,
                BorderColor = BorderColor.IntOpaqueBlack,
                UnnormalizedCoordinates = false,
                CompareEnable = false,
                CompareOp = CompareOp.Always,
                MipmapMode = SamplerMipmapMode.Linear,
                MipLodBias = 0.0f,
                MinLod = 0.0f,
                MaxLod = (float)mipLevels
            };

            return context.SamplerCache.GetSampler(samplerCreateInfo);
        }

        // Rest of existing methods remain the same...
        private static unsafe void CopyImageData(VulkanContext context, UploadBatch batch,
                                             SKBitmap skBitmap, VkImage vkImage)
        {
            CopyImageDataFromPointer(batch, vkImage, skBitmap.GetPixelSpan(),
                                    (uint)skBitmap.Width, (uint)skBitmap.Height,
                                    GetVulkanFormat(skBitmap.ColorType, context));
        }

        private static unsafe void CopyImageDataFromPointer(
             UploadBatch batch,
             VkImage vkImage,
             Span<byte> data,
             uint width,
             uint height,
             Format format)
        {
            var imageSize = (ulong)(width * height * format.GetBytesPerPixel());

            var preCopyBarrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.Undefined,
                NewLayout = ImageLayout.TransferDstOptimal,
                Image = vkImage,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = vkImage.AspectFlags,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                SrcAccessMask = AccessFlags.None,
                DstAccessMask = AccessFlags.TransferWriteBit
            };

            batch.PipelineBarrier(
                srcStage: PipelineStageFlags.TopOfPipeBit,
                dstStage: PipelineStageFlags.TransferBit,
                imageMemoryBarriers: new[] { preCopyBarrier }
            );

            if (!batch.SubmitContext.StagingManager.TryStage<byte>(batch, data, out var offset, out var size))
            {
                throw new Exception("Failed to stage data from image");
            }

            var bufferBarrier = new BufferMemoryBarrier
            {
                SType = StructureType.BufferMemoryBarrier,
                SrcAccessMask = AccessFlags.HostWriteBit,
                DstAccessMask = AccessFlags.TransferReadBit,
                Buffer = batch.SubmitContext.StagingManager.StagingBuffer,
                Offset = offset,
                Size = size
            };

            batch.PipelineBarrier(
                srcStage: PipelineStageFlags.HostBit,
                dstStage: PipelineStageFlags.TransferBit,
                bufferMemoryBarriers: new[] { bufferBarrier }
            );

            var copyRegion = new BufferImageCopy
            {
                BufferOffset = offset,
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = vkImage.AspectFlags,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                ImageExtent = new Extent3D(width, height, 1)
            };

            batch.CommandBuffer.CopyBufferToImage(
                srcBuffer: batch.SubmitContext.StagingManager.StagingBuffer,
                dstImage: vkImage,
                dstImageLayout: ImageLayout.TransferDstOptimal,
                regionCount: 1,
                pRegions: &copyRegion
            );
        }

        public static Texture2D LoadFromSKImage(VulkanContext context, SKBitmap skImage, string? name = null)
        {
            var width = (uint)skImage.Width;
            var height = (uint)skImage.Height;
            var format = GetVulkanFormat(skImage.Info.ColorType, context);
            var vkImage = CreateVulkanImage(context, width, height, format);

            var transferComplete = VkSemaphore.Create(context);
            var graphicsComplete = VkSemaphore.Create(context);

            var transferBatch = context.TransferSubmitContext.CreateBatch();
            CopyImageDataFromPointer(transferBatch, vkImage, skImage.GetPixelSpan(),
                                    width, height, format);
            transferBatch.AddSignalSemaphore(transferComplete);
            using (var transferOp = context.TransferSubmitContext.FlushSingle(transferBatch, VkFence.CreateNotSignaled(context)))
            {
                transferOp.Wait();
            }

            var graphicsBatch = context.GraphicsSubmitContext.CreateBatch();
            graphicsBatch.AddWaitSemaphore(transferComplete, PipelineStageFlags.TransferBit);
            vkImage.TransitionImageLayout(
                graphicsBatch.CommandBuffer,
                ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.FragmentShaderBit
            );
            graphicsBatch.AddSignalSemaphore(graphicsComplete);
            graphicsBatch.Submit();

            var sampler = CreateSampler(context, 1);
            var texture = new Texture2D(context, vkImage, sampler);

            if (!string.IsNullOrEmpty(name))
            {
                vkImage.LabelObject(name);
            }

            skImage.Dispose();
            return texture;
        }

        // Default texture getters
        internal static Texture GetDefaultAlbedoTexture(VulkanContext context)
        {
            return GetEmptyTexture(context);
        }

        internal static Texture GetDefaultNormalTexture(VulkanContext context)
        {
            return GetEmptyNormalTexture(context);
        }

        internal static Texture GetDefaultMRATexture(VulkanContext context)
        {
            return GetEmptyMRATexture(context);
        }

        internal static Texture2D GetDefaultLUTTexture(VulkanContext context)
        {
            return GetEmptyTexture(context);
        }

        public static Texture2D Create(VulkanContext context,
                                       int width,
                                       int height,
                                       Format format,
                                       Span<byte> data)
        {
            var vkImage = CreateVulkanImage(context, (uint)width, (uint)height, format);

            // Create semaphores for queue synchronization
            var transferComplete = VkSemaphore.Create(context);
            var graphicsComplete = VkSemaphore.Create(context);

            // Transfer queue operations
            var transferBatch = context.TransferSubmitContext.CreateBatch();
            CopyImageDataFromPointer(transferBatch, vkImage, data, (uint)width, (uint)height, format);
            transferBatch.AddSignalSemaphore(transferComplete);
            using (var transferOp = context.TransferSubmitContext.FlushSingle(transferBatch, VkFence.CreateNotSignaled(context)))
            {
                transferOp.Wait();
            }

            // Graphics queue operations
            var graphicsBatch = context.GraphicsSubmitContext.CreateBatch();
            graphicsBatch.AddWaitSemaphore(transferComplete, PipelineStageFlags.TransferBit);
            vkImage.TransitionImageLayout(
                graphicsBatch.CommandBuffer,
                ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.FragmentShaderBit
            );
            graphicsBatch.AddSignalSemaphore(graphicsComplete);
            graphicsBatch.Submit();

            var sampler = CreateSampler(context, vkImage.MipLevels);
            return new Texture2D(context, vkImage, sampler);
        }

        public static Texture2D CreateShadowMapArray(VulkanContext context,
                                                     uint size,
                                                     uint arrayLayers,
                                                     Format format = Format.D32Sfloat,
                                                     ImageUsageFlags usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit,
                                                     string? name = null)
        {

            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = format,
                Extent = new Extent3D(size, size, 1),
                MipLevels = 1,
                ArrayLayers = arrayLayers,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined,
                Flags = ImageCreateFlags.None
            };

            var image = VkImage.Create(context, imageInfo, MemoryPropertyFlags.DeviceLocalBit, ImageAspectFlags.DepthBit);

            // Create depth-specific sampler for array texture
            var samplerCreateInfo = new SamplerCreateInfo
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

            var sampler = context.SamplerCache.GetSampler(samplerCreateInfo);

            // Transition ALL layers to shader read-only optimal
            var batch = context.GraphicsSubmitContext.CreateBatch();
            image.TransitionImageLayout(
                batch.CommandBuffer,
                ImageLayout.ShaderReadOnlyOptimal,
                baseMipLevel: 0,
                levelCount: 1,
                baseArrayLayer: 0,
                layerCount: arrayLayers  // Transition ALL layers
            );
            batch.Submit();

            if (!string.IsNullOrEmpty(name))
            {
                image.LabelObject(name);
            }

            return new Texture2D(context, image, sampler);
        }

        public static Texture2D CreatePointShadowMapArray(VulkanContext context,uint size,
                             uint arrayLayers, // Number of cube maps in the array
                             Format format = Format.D32Sfloat,
                             ImageUsageFlags usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit,
                             string? name = null)
        {

            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = format,
                Extent = new Extent3D(size, height: size, 1),
                MipLevels = 1,
                ArrayLayers = 6 * arrayLayers, // Each cube has 6 faces
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined,
                Flags = ImageCreateFlags.CreateCubeCompatibleBit
            };

            var image = VkImage.Create(context, imageInfo, MemoryPropertyFlags.DeviceLocalBit, ImageAspectFlags.DepthBit);

            // Create depth-specific sampler for cube array
            var samplerCreateInfo = new SamplerCreateInfo
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

            var sampler = context.SamplerCache.GetSampler(samplerCreateInfo);

            // Transition ALL layers to shader read-only optimal
            var batch = context.GraphicsSubmitContext.CreateBatch();
            image.TransitionImageLayout(
                batch.CommandBuffer,
                ImageLayout.ShaderReadOnlyOptimal,
                baseMipLevel: 0,
                levelCount: 1,
                baseArrayLayer: 0,
                layerCount: 6 * arrayLayers  // Transition ALL layers, not just the first one
            );
            batch.Submit();

            if (!string.IsNullOrEmpty(name))
            {
                image.LabelObject(name);
            }

            return new Texture2D(context, image, sampler);
        }
    }
}