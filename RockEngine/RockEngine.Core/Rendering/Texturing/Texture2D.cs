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

        public uint Width => _image.Extent.Width;
        public uint Height => _image.Extent.Height;

        public Texture2D(VulkanContext context, VkImage image,
                        VkSampler sampler, string? sourcePath = null)
            : base(context, image,  sampler, sourcePath) { }
        public static async Task<Texture2D> CreateAsync(VulkanContext context, byte[] data, string name, CancellationToken cancellationToken = default)
        {
            return await CreateFromBytesAsync(context, data, name, cancellationToken);
        }

        public static async Task<Texture2D> CreateAsync(VulkanContext context, Stream stream, string name, CancellationToken cancellationToken = default)
        {
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);
            var bytes = memoryStream.ToArray();

            return await CreateFromBytesAsync(context, bytes, name,  cancellationToken);
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
            skBitmap.Dispose(); // Always dispose original bitmap
            return texture;
        }

        // Use GPU mipmap generation whenever possible
        public static async Task<Texture2D> CreateFromSkBitmapAsync(VulkanContext context,
            SKBitmap skBitmap, string name, CancellationToken cancellationToken = default)
        {
            var width = (uint)skBitmap.Width;
            var height = (uint)skBitmap.Height;
            var format = GetVulkanFormat(skBitmap.Info.ColorType, context);
            uint totalMipLevels = CalculateMipLevels(width, height);


            var image = CreateVulkanImage(context, width, height, format, totalMipLevels);

            // Upload and generate mips in a single operation
            var transferComplete = VkSemaphore.Create(context);
            var graphicsComplete = VkSemaphore.Create(context);


            // Upload base level
            var transferBatch = context.TransferSubmitContext.CreateBatch();
            CopyImageData(context, transferBatch, skBitmap, image);
            transferBatch.AddSignalSemaphore(transferComplete);

            await context.TransferSubmitContext.FlushSingle(transferBatch,
                VkFence.CreateNotSignaled(context));

            // Generate all mip levels on GPU
            var graphicsBatch = context.GraphicsSubmitContext.CreateBatch();
            graphicsBatch.AddWaitSemaphore(transferComplete, PipelineStageFlags.TransferBit);

            if (totalMipLevels > 1)
            {
                image.GenerateMipmaps(graphicsBatch.CommandBuffer);
            }
            else
            {
                // Transition single level
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

        public static Texture2D Create(VulkanContext context, int width, int height,
                                      Format format, Span<byte> data)
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
           /* using (var graphicsOp = context.GraphicsSubmitContext.FlushSingle(graphicsBatch, VkFence.CreateNotSignaled(context)))
            {
                graphicsOp.Wait();
            }*/

            var sampler = CreateSampler(context, vkImage.MipLevels);
            return new Texture2D(context, vkImage,  sampler);
        }

      
        private static VkImage CreateVulkanImage(
            VulkanContext context,
            uint width,
            uint height,
            Format format,
            uint mipLevels = 1)
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
                Usage = ImageUsageFlags.TransferSrcBit |
                        ImageUsageFlags.TransferDstBit |
                        ImageUsageFlags.SampledBit,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined
            };

            return VkImage.Create(context, imageInfo, MemoryPropertyFlags.DeviceLocalBit, ImageAspectFlags.ColorBit);
        }

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

            // Transition to DST_OPTIMAL for copying
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

            // Copy data
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


        public static Texture2D GetEmptyTexture(VulkanContext context)
        {
            _emptyTexture ??= CreateColorTexture(context, new Vector4D<byte>(211, 211, 211, 1));
            return _emptyTexture;
        }

        public static Texture2D CreateColorTexture(VulkanContext context, Vector4D<byte> colors)
        {
            using var surface = SKSurface.Create(new SKImageInfo(1, 1, SKColorType.Srgba8888));
            surface.Canvas.Clear(new SKColor(colors.X, colors.Y, colors.Z, colors.W));
            using var image = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);
            return LoadFromSKImage(context, bitmap);
        }

        public static Texture2D LoadFromSKImage(VulkanContext context, SKBitmap skImage)
        {
            var width = (uint)skImage.Width;
            var height = (uint)skImage.Height;
            var format = GetVulkanFormat(skImage.Info.ColorType, context);
            var vkImage = CreateVulkanImage(context, width, height, format);

            // Create semaphores for queue synchronization
            var transferComplete = VkSemaphore.Create(context);
            var graphicsComplete = VkSemaphore.Create(context);

            // Upload data via transfer queue
            var transferBatch = context.TransferSubmitContext.CreateBatch();
            CopyImageDataFromPointer(transferBatch, vkImage, skImage.GetPixelSpan(),
                                    width, height, format);
            transferBatch.AddSignalSemaphore(transferComplete);
            using (var transferOp = context.TransferSubmitContext.FlushSingle(transferBatch, VkFence.CreateNotSignaled(context)))
            {
                transferOp.Wait();
            }

            // Transition in graphics queue
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
            var texture = new Texture2D(context, vkImage, sampler);

            skImage.Dispose();
            return texture;
        }

        internal static Texture GetDefaultAlbedoTexture(VulkanContext context)
        {
            return Texture2D.GetEmptyTexture(context);
        }

        internal static Texture GetDefaultNormalTexture(VulkanContext context)
        {
            return Texture2D.GetEmptyTexture(context);
        }

        internal static Texture GetDefaultMRATexture(VulkanContext context)
        {
            return Texture2D.GetEmptyTexture(context);

        }

        internal static Texture2D GetDefaultLUTTexture(VulkanContext context)
        {
            return Texture2D.GetEmptyTexture(context);
        }
    }
}
