using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Texturing
{
    public class StreamableTexture : Texture
    {
        internal readonly Lock _syncRoot = new();

        public bool IsFullyLoaded => LoadedMipLevels == TotalMipLevels;
        private readonly uint _currentMaxMipLevel;

        public StreamableTexture(
            VulkanContext context,
            VkImage image,
            VkImageView view,
            VkSampler sampler, string sourcePath)
            : base(context, image, view, sampler, sourcePath)
        {
            _currentMaxMipLevel = TotalMipLevels - 1; // Start with lowest quality
            LoadedMipLevels = 1;
        }

        public unsafe void UpdateMipLevel(uint targetMip, IntPtr data, ulong size)
        {
            lock (_syncRoot)
            {
                // Ensure mipLevel is within valid range
                if (targetMip >= TotalMipLevels)
                    throw new ArgumentOutOfRangeException(nameof(targetMip),
                        "Mip level exceeds total available mip levels");

                var oldView = _imageView;
                var oldSampler = _sampler;

                _context.SubmitSingleTimeCommand(cmd =>
                {
                    Image.TransitionMipLayout(cmd, ImageLayout.TransferDstOptimal, targetMip);
                    CopyDataToMip(cmd, data, size, targetMip);
                    Image.TransitionMipLayout(cmd, ImageLayout.ShaderReadOnlyOptimal, targetMip);
                });

                // Clamp loaded levels to never exceed total mips
                LoadedMipLevels = Math.Min(targetMip + 1, TotalMipLevels);

                _context.AddDisposal(oldView);

                // Create view for loaded mips (base=0, levelCount=LoadedMipLevels)
                _imageView = Image.CreateView(
                    ImageAspectFlags.ColorBit,
                    mipLevels: LoadedMipLevels,
                    baseMipLevel: 0
                );

                // Sampler uses actual loaded mip count
                _sampler = CreateSampler(_context, LoadedMipLevels);
                NotifyTextureUpdated();
            }
        }


        public ValueTask StreamNextMipAsync(TextureStreamer streamer, float priority)
        {
            if (IsFullyLoaded) return default;

            var nextMip = LoadedMipLevels;
            streamer.RequestStream(this, nextMip, priority);
            return default;
        }

        public void EvictMip(uint mipLevel)
        {
            lock (_syncRoot)
            {
                if (mipLevel >= LoadedMipLevels) return;

                // Transition mip to undefined layout
                _context.SubmitSingleTimeCommand((cmd) =>
                {
                    Image.TransitionMipLayout(cmd, ImageLayout.Undefined, mipLevel);
                    LoadedMipLevels = mipLevel;
                });
            }
        }

        private unsafe void CopyDataToMip(
            VkCommandBuffer cmd,
            IntPtr data,
            ulong size,
            uint mipLevel)
        {
            var staging = VkBuffer.CreateAndCopyToStagingBuffer(_context, (void*)data, size);

            var region = new BufferImageCopy
            {
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = mipLevel,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                ImageExtent = new Extent3D(
                    Math.Max(Image.Width >> (int)mipLevel, 1),
                    Math.Max(Image.Height >> (int)mipLevel, 1),
                    1)
            };

            VulkanContext.Vk.CmdCopyBufferToImage(cmd,
                staging,
                Image,
                ImageLayout.TransferDstOptimal,
                1, &region);
        }

    }
}
