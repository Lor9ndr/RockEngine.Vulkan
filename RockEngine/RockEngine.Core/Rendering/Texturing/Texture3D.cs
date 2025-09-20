using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using SkiaSharp;

using System.Threading.Tasks;

namespace RockEngine.Core.Rendering.Texturing
{
    public sealed class Texture3D : Texture
    {
        public uint Width => _image.Extent.Width;
        public uint Height => _image.Extent.Height;
        public uint Depth => _image.Extent.Depth;

        public Texture3D(VulkanContext context, VkImage image,
                        VkSampler sampler, string? sourcePath = null)
            : base(context, image,  sampler, sourcePath) { }

        public static async Task<Texture3D> CreateCubeMapAsync(VulkanContext context, string[] facePaths,
                                                              bool generateMipMaps = false,
                                                              CancellationToken cancellationToken = default)
        {
            if (facePaths.Length != 6)
                throw new ArgumentException("Cube map requires exactly 6 face paths.");

            // Load face bitmaps
            var faceBitmaps = new SKBitmap[6];
            for (int i = 0; i < 6; i++)
            {
                var bytes = await File.ReadAllBytesAsync(facePaths[i], cancellationToken);
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

            // Create image with initial layout as TransferDstOptimal
            var image = CreateVulkanImage(context, width, height, format, mipLevels);

            // Create semaphores for queue synchronization
            var transferComplete = VkSemaphore.Create(context);
            var graphicsComplete = VkSemaphore.Create(context);

            // Transfer queue operations
            var transferBatch = context.TransferSubmitContext.CreateBatch();
            transferBatch.CommandBuffer.LabelObject("CubeMap Transfer");

            // Transition to TransferDstOptimal (even though we created it with this layout, this ensures tracking)
            image.TransitionImageLayout(
                transferBatch.CommandBuffer,
                ImageLayout.TransferDstOptimal,
                baseMipLevel: 0,
                levelCount: 1,
                baseArrayLayer: 0,
                layerCount: 6
            );

            // Upload each face
            for (int i = 0; i < 6; i++)
            {
                var pixelData = faceBitmaps[i].GetPixelSpan();
                if (!transferBatch.SubmitContext.StagingManager.TryStage(transferBatch, pixelData,
                                                                  out ulong bufferOffset,
                                                                  out ulong stagedSize))
                {
                    throw new InvalidOperationException("Staging buffer overflow");
                }

                // Barrier for staging buffer
                var bufferBarrier = new BufferMemoryBarrier
                {
                    SType = StructureType.BufferMemoryBarrier,
                    SrcAccessMask = AccessFlags.HostWriteBit,
                    DstAccessMask = AccessFlags.TransferReadBit,
                    Buffer = transferBatch.SubmitContext.StagingManager.StagingBuffer,
                    Offset = bufferOffset,
                    Size = stagedSize
                };

                transferBatch.PipelineBarrier(
                    srcStage: PipelineStageFlags.HostBit,
                    dstStage: PipelineStageFlags.TransferBit,
                    bufferMemoryBarriers: new[] { bufferBarrier }
                );

                // Copy to image
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

                transferBatch.CommandBuffer.CopyBufferToImage(
                    srcBuffer: transferBatch.SubmitContext.StagingManager.StagingBuffer,
                    dstImage: image,
                    dstImageLayout: ImageLayout.TransferDstOptimal,
                    regionCount: 1,
                    pRegions: in copyRegion
                );
            }

            transferBatch.AddSignalSemaphore(transferComplete);
            using (var transferOp =  context.TransferSubmitContext.FlushSingle(transferBatch, VkFence.CreateNotSignaled(context)))
            {
                await transferOp.WaitAsync();
            }

            // Graphics queue operations
            var graphicsBatch = context.GraphicsSubmitContext.CreateBatch();
            graphicsBatch.CommandBuffer.LabelObject("CubeMap Graphics");
            graphicsBatch.AddWaitSemaphore(transferComplete, PipelineStageFlags.TransferBit);

            if (generateMipMaps)
            {
                // Prepare base level for mipmap generation
                image.TransitionImageLayout(
                    graphicsBatch.CommandBuffer,
                    ImageLayout.TransferSrcOptimal,
                    baseMipLevel: 0,
                    levelCount: 1,
                    baseArrayLayer: 0,
                    layerCount: 6
                );

                // Generate mipmaps
                image.GenerateMipmaps(graphicsBatch.CommandBuffer);
            }
            else
            {
                // Transition directly to shader read layout
                image.TransitionImageLayout(
                    graphicsBatch.CommandBuffer,
                    ImageLayout.ShaderReadOnlyOptimal,
                    baseMipLevel: 0,
                    levelCount: 1,
                    baseArrayLayer: 0,
                    layerCount: 6
                );
            }

            graphicsBatch.AddSignalSemaphore(graphicsComplete);
            await context.GraphicsSubmitContext.FlushSingle(graphicsBatch, VkFence.CreateNotSignaled(context)).WaitAsync(cancellationToken);

            // Create image view and sampler
            var sampler = CreateSampler(context, mipLevels);

            // Cleanup
            foreach (var bitmap in faceBitmaps)
            {
                bitmap.Dispose();
            }
            return new Texture3D(context, image, sampler);
        }

        private static VkImage CreateVulkanImage(
            VulkanContext context,
            uint width,
            uint height,
            Format format,
            uint mipLevels,
            ImageLayout initialLayout = ImageLayout.Undefined)
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
                InitialLayout = initialLayout,
                Flags = ImageCreateFlags.CreateCubeCompatibleBit
            };

            return VkImage.Create(context, imageInfo, MemoryPropertyFlags.DeviceLocalBit, ImageAspectFlags.ColorBit);
        }
    }
}