using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using SkiaSharp;

namespace RockEngine.Core.Rendering.Texturing
{
    public sealed class Texture3D : Texture
    {
        public uint Width => _image.Extent.Width;
        public uint Height => _image.Extent.Height;
        public uint Depth => _image.Extent.Depth;

        public Texture3D(VulkanContext context, VkImage image, VkImageView imageView,
                        VkSampler sampler, string? sourcePath = null)
            : base(context, image, imageView, sampler, sourcePath) { }

        public static async Task<Texture3D> CreateCubeMapAsync(VulkanContext context, string[] facePaths,
                                                              bool generateMipMaps = false,
                                                              CancellationToken cancellationToken = default)
        {
            if (facePaths.Length != 6)
                throw new ArgumentException("Cube map requires exactly 6 face paths.");

            var faceBitmaps = new SKBitmap[6];
            for (int i = 0; i < 6; i++)
            {
                var bytes =  await File.ReadAllBytesAsync(facePaths[i], cancellationToken);
                faceBitmaps[i] = SKBitmap.Decode(bytes);
                if (faceBitmaps[i].Width != faceBitmaps[0].Width ||
                    faceBitmaps[i].Height != faceBitmaps[0].Height)
                    throw new InvalidOperationException("Cube map faces must have uniform dimensions.");
                if (faceBitmaps[i].ColorType != faceBitmaps[0].ColorType)
                    throw new InvalidOperationException("Cube map faces must have the same color format.");
            }

            uint width = (uint)faceBitmaps[0].Width;
            uint height = (uint)faceBitmaps[0].Height;
            var format = GetVulkanFormat(faceBitmaps[0].ColorType, context);
            uint mipLevels = generateMipMaps ? CalculateMipLevels(width, height) : 1;

            var image = CreateVulkanImage(
                context,
                width,
                height,
                format,
                mipLevels);

            var uploadBatch = context.SubmitContext.CreateBatch();
            uploadBatch.CommandBuffer.LabelObject("CreateCubeMapAsync cmd");

            image.TransitionImageLayout(
                uploadBatch.CommandBuffer,
                ImageLayout.TransferDstOptimal,
                baseMipLevel: 0,
                levelCount: 1,
                baseArrayLayer: 0,
                layerCount: 6);

            for (int i = 0; i < 6; i++)
            {
                var pixelData = faceBitmaps[i].GetPixelSpan();
                if (!context.SubmitContext.StagingManager.TryStage(uploadBatch, pixelData,
                                                                  out ulong bufferOffset,
                                                                  out ulong stagedSize))
                {
                    throw new InvalidOperationException("Staging buffer overflow");
                }

                var bufferBarrier = new BufferMemoryBarrier
                {
                    SType = StructureType.BufferMemoryBarrier,
                    SrcAccessMask = AccessFlags.HostWriteBit,
                    DstAccessMask = AccessFlags.TransferReadBit,
                    Buffer = context.SubmitContext.StagingManager.StagingBuffer,
                    Offset = bufferOffset,
                    Size = stagedSize
                };

                uploadBatch.PipelineBarrier(
                    srcStage: PipelineStageFlags.HostBit,
                    dstStage: PipelineStageFlags.TransferBit,
                    bufferMemoryBarriers: new[] { bufferBarrier }
                );

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

            if (generateMipMaps)
            {
                // Барьер перед генерацией мипмапов
                var barrier = new ImageMemoryBarrier
                {
                    SType = StructureType.ImageMemoryBarrier,
                    Image = image,
                    OldLayout = ImageLayout.TransferDstOptimal,
                    NewLayout = ImageLayout.TransferSrcOptimal,
                    SubresourceRange = new ImageSubresourceRange
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        BaseMipLevel = 0,
                        LevelCount = 1,
                        BaseArrayLayer = 0,
                        LayerCount = 6
                    },
                    SrcAccessMask = AccessFlags.TransferWriteBit,
                    DstAccessMask = AccessFlags.TransferReadBit
                };

                uploadBatch.PipelineBarrier(
                    srcStage: PipelineStageFlags.TransferBit,
                    dstStage: PipelineStageFlags.TransferBit,
                    imageMemoryBarriers: new[] { barrier }
                );

                // Генерация мипмапов
                image.GenerateMipmaps(uploadBatch.CommandBuffer);

                // Барьер после генерации мипмапов
                var finalBarrier = new ImageMemoryBarrier
                {
                    SType = StructureType.ImageMemoryBarrier,
                    Image = image,
                    OldLayout = ImageLayout.TransferSrcOptimal,
                    NewLayout = ImageLayout.ShaderReadOnlyOptimal,
                    SubresourceRange = new ImageSubresourceRange
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        BaseMipLevel = 0,
                        LevelCount = mipLevels,
                        BaseArrayLayer = 0,
                        LayerCount = 6
                    },
                    SrcAccessMask = AccessFlags.TransferWriteBit,
                    DstAccessMask = AccessFlags.ShaderReadBit
                };

                uploadBatch.PipelineBarrier(
                    srcStage: PipelineStageFlags.TransferBit,
                    dstStage: PipelineStageFlags.FragmentShaderBit,
                    imageMemoryBarriers: new[] { finalBarrier }
                );
            }
            else
            {
                image.TransitionImageLayout(
                        uploadBatch.CommandBuffer,
                        ImageLayout.ShaderReadOnlyOptimal,
                        baseMipLevel: 0,
                        levelCount: 1,
                        baseArrayLayer: 0,
                        layerCount: 6);
            }

            await context.SubmitContext.FlushSingle(uploadBatch,VkFence.CreateNotSignaled(context) );


            var imageView = CreateCubeMapImageView(context, image, format);
            var sampler = CreateSampler(context, mipLevels);

            foreach (var item in faceBitmaps)
            {
                item.Dispose();
            }


            return new Texture3D(context, image, imageView, sampler);
        }

        private static VkImage CreateVulkanImage(
            VulkanContext context,
            uint width,
            uint height,
            Format format,
            uint mipLevels = 1)
        {
            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = format,
                Extent = new Extent3D(width, height, 1),
                MipLevels = mipLevels,
                ArrayLayers = 6,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.TransferDstBit |
                        ImageUsageFlags.TransferSrcBit |
                        ImageUsageFlags.SampledBit,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined,
                Flags = ImageCreateFlags.CreateCubeCompatibleBit
            };

            return VkImage.Create(context, imageInfo, MemoryPropertyFlags.DeviceLocalBit, ImageAspectFlags.ColorBit);
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

            if (image.GetMipLayout(0, 0) != ImageLayout.ShaderReadOnlyOptimal)
            {
                throw new InvalidOperationException("Cubemap image must be in SHADER_READ_ONLY layout");
            }

            return VkImageView.Create(context, image, viewInfo);
        }
    }
}
