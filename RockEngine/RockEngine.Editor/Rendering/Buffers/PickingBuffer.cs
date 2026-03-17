using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Numerics;

namespace RockEngine.Editor.Rendering.Buffers
{
    public class PickingBuffer : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly VkBuffer _stagingBuffer;
        private bool _disposed;

        public PickingBuffer(VulkanContext context)
        {
            _context = context;

            // Create staging buffer for reading pixels (RGBA - 4 bytes)
            _stagingBuffer = VkBuffer.Create(
                context,
                4, // 4 bytes for RGBA
                BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        }

        public async ValueTask<Vector4> ReadPixelAsync(Texture2D sourceTexture, uint x, uint y, bool flipY = true)
        {
            var batch = _context.GraphicsSubmitContext.CreateBatch();
            // Ensure the source texture is in correct layout
            //if(sourceTexture.Image.GetMipLayout(0) != ImageLayout.TransferSrcOptimal)
            {
                sourceTexture.Image.TransitionImageLayout(batch, ImageLayout.Undefined, ImageLayout.TransferSrcOptimal);
            }
            uint actualY = flipY ? (sourceTexture.Height - 1 - y) : y;
            // Copy specific pixel region
            var imageCopy = new BufferImageCopy
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
                ImageOffset = new Offset3D((int)x, (int)actualY, 0),
                ImageExtent = new Extent3D(1, 1, 1)
            };

            batch.CopyImageToBuffer(
                sourceTexture.Image,
                ImageLayout.TransferSrcOptimal,
                _stagingBuffer,
                in imageCopy);

            // Add barrier to ensure copy completes before reading
            var barrier = new BufferMemoryBarrier2
            {
                SType = StructureType.BufferMemoryBarrier2,
                SrcAccessMask = AccessFlags2.TransferWriteBit,
                DstAccessMask = AccessFlags2.HostReadBit,
                Buffer = _stagingBuffer,
                Offset = 0,
                Size = Vk.WholeSize
            };

            batch.PipelineBarrier([], [barrier], []);
            await batch.SubmitContext.SubmitSingle(batch, VkFence.CreateNotSignaled(_context));

            return ReadPixelData();
        }
        public Vector4 ReadPixel(Texture2D sourceTexture, uint x, uint y, bool flipY = true)
        {
            var batch = _context.GraphicsSubmitContext.CreateBatch();
            // Ensure the source texture is in correct layout
            //if(sourceTexture.Image.GetMipLayout(0) != ImageLayout.TransferSrcOptimal)
            {
                sourceTexture.Image.TransitionImageLayout(batch, ImageLayout.Undefined, ImageLayout.TransferSrcOptimal);
            }
            uint actualY = flipY ? (sourceTexture.Height - 1 - y) : y;
            // Copy specific pixel region
            var imageCopy = new BufferImageCopy
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
                ImageOffset = new Offset3D((int)x, (int)actualY, 0),
                ImageExtent = new Extent3D(1, 1, 1)
            };

            batch.CopyImageToBuffer(
                sourceTexture.Image,
                ImageLayout.TransferSrcOptimal,
                _stagingBuffer,
                in imageCopy);

            // Add barrier to ensure copy completes before reading
            var barrier = new BufferMemoryBarrier2
            {
                SType = StructureType.BufferMemoryBarrier2,
                SrcAccessMask = AccessFlags2.TransferWriteBit,
                DstAccessMask = AccessFlags2.HostReadBit,
                Buffer = _stagingBuffer,
                Offset = 0,
                Size = Vk.WholeSize,
                SrcStageMask = PipelineStageFlags2.TransferBit,
                DstStageMask = PipelineStageFlags2.HostBit
            };

            batch.PipelineBarrier([], 
                [barrier], []);
            batch.SubmitContext.SubmitSingle(batch, VkFence.CreateNotSignaled(_context)).Wait();

            return ReadPixelData();
        }

        private Vector4 ReadPixelData()
        {
            using var mappedMemory = _stagingBuffer.MapMemory(4, 0);
            var data = mappedMemory.GetSpan<byte>();

            return new Vector4(
                data[0] / 255.0f,
                data[1] / 255.0f,
                data[2] / 255.0f,
                data[3] / 255.0f
            );
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _stagingBuffer?.Dispose();
                _disposed = true;
            }
        }
    }
}