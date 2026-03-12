using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Texturing
{
    public sealed class Texture3D : Texture
    {
        public uint Width => _image.Extent.Width;
        public uint Height => _image.Extent.Height;
        public uint Depth => _image.Extent.Depth;

        public Texture3D(VulkanContext context, VkImage image,
                        VkSampler sampler)
            : base(context, image,  sampler) { }

   

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

        internal static Texture3D GetDefaultCubemapTexture(VulkanContext context)
        {
            // Create a simple 16x16 default cubemap with colored faces
            const uint SIZE = 16;
            var format = Format.R8G8B8A8Unorm;
            uint mipLevels = 1;

            // Define colors for each face (RGBA)
            var faceColors = new byte[][]
            {
                [255, 0, 0, 255],       // Right  - Red
                [0, 255, 0, 255],       // Left   - Green
                [0, 0, 255, 255],       // Top    - Blue
                [255, 255, 0, 255],     // Bottom - Yellow
                [255, 0, 255, 255],     // Front  - Magenta
                [0, 255, 255, 255]      // Back   - Cyan
            };

            // Create image
            var image = CreateVulkanImage(context, SIZE, SIZE, format, mipLevels, ImageLayout.Undefined);

            // Create staging resources
            var stagingBuffer = VkBuffer.Create(
                context,
                SIZE * SIZE * 4 * 6, // 6 faces, each with SIZE*SIZE pixels * 4 bytes per pixel
                BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit
            );

            // Create semaphores for synchronization
            var transferComplete = VkSemaphore.Create(context);
            var graphicsComplete = VkSemaphore.Create(context);

            try
            {
                // Fill staging buffer
                using (var mappedMemory = stagingBuffer.MapMemory(SIZE * SIZE * 4 * 6, 0))
                {
                    var span = mappedMemory.GetSpan<byte>();

                    for (int face = 0; face < 6; face++)
                    {
                        var faceColor = faceColors[face];
                        int faceOffset = face * (int)(SIZE * SIZE * 4);

                        for (int y = 0; y < SIZE; y++)
                        {
                            for (int x = 0; x < SIZE; x++)
                            {
                                int pixelOffset = faceOffset + (y * (int)SIZE + x) * 4;
                                span[pixelOffset] = faceColor[0];     // R
                                span[pixelOffset + 1] = faceColor[1]; // G
                                span[pixelOffset + 2] = faceColor[2]; // B
                                span[pixelOffset + 3] = faceColor[3]; // A
                            }
                        }
                    }
                }

                // Transfer queue operations - only for copying data
                var transferBatch = context.TransferSubmitContext.CreateBatch();
                transferBatch.LabelObject("DefaultCubeMap Transfer");

                // Transition image to TransferDstOptimal on transfer queue
                image.TransitionImageLayout(
                    transferBatch,
                     ImageLayout.Undefined,
                    ImageLayout.TransferDstOptimal,
                    baseMipLevel: 0,
                    levelCount: 1,
                    baseArrayLayer: 0,
                    layerCount: 6
                );

                // Copy data for each face
                for (int face = 0; face < 6; face++)
                {
                    var copyRegion = new BufferImageCopy
                    {
                        BufferOffset = (ulong)(face * SIZE * SIZE * 4),
                        ImageSubresource = new ImageSubresourceLayers
                        {
                            AspectMask = ImageAspectFlags.ColorBit,
                            MipLevel = 0,
                            BaseArrayLayer = (uint)face,
                            LayerCount = 1
                        },
                        ImageExtent = new Extent3D(SIZE, SIZE, 1)
                    };

                    transferBatch.CopyBufferToImage(
                        srcBuffer: stagingBuffer,
                        dstImage: image,
                        dstImageLayout: ImageLayout.TransferDstOptimal,
                        pRegions: in copyRegion
                    );
                }

                // Signal transfer completion
                transferBatch.AddSignalSemaphore(transferComplete);

                // Submit transfer operations
                using (var transferOp = context.TransferSubmitContext.SubmitSingle(transferBatch, VkFence.CreateNotSignaled(context)))
                {
                    transferOp.Wait();
                }

                // Graphics queue operations - for layout transitions that require graphics pipeline stages
                var graphicsBatch = context.GraphicsSubmitContext.CreateBatch();
                graphicsBatch.LabelObject("DefaultCubeMap Graphics");

                // Wait for transfer to complete
                graphicsBatch.AddWaitSemaphore(transferComplete, PipelineStageFlags.TransferBit);

                // Transition to ShaderReadOnlyOptimal on graphics queue
                image.TransitionImageLayout(
                    graphicsBatch,
                     ImageLayout.TransferDstOptimal,
                    ImageLayout.ShaderReadOnlyOptimal,
                    baseMipLevel: 0,
                    levelCount: 1,
                    baseArrayLayer: 0,
                    layerCount: 6
                );

                // Signal graphics completion
                graphicsBatch.AddSignalSemaphore(graphicsComplete);

                // Submit graphics operations
                context.GraphicsSubmitContext.SubmitSingle(graphicsBatch, VkFence.CreateNotSignaled(context)).Wait();

                // Create sampler
                var sampler = CreateSampler(context, mipLevels);

                return new Texture3D(context, image, sampler);
            }
            finally
            {
                stagingBuffer.Dispose();
                transferComplete.Dispose();
                graphicsComplete.Dispose();
            }
        }
    }
}