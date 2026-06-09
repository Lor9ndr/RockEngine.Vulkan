using RockEngine.Vulkan;
using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Texturing.Atlasing
{
    public class Atlas : IDisposable
    {
        public Texture2D Texture { get; }
        public uint Width { get; }
        public uint Height { get; }
        public IntPtr TextureId { get; }   // Bindless index in global array

        private readonly IAtlasAllocator _allocator;
        private readonly Lock _lock = new();

        /// <summary>
        /// Creates a new atlas page.
        /// </summary>
        /// <param name="vk">Vulkan context</param>
        /// <param name="width">Page width in pixels</param>
        /// <param name="height">Page height in pixels</param>
        /// <param name="format">Texture format (must match what you will pack)</param>
        /// <param name="allocator">Packing algorithm (Grid, Guillotine, MaxRects)</param>
        /// <param name="registerTexture">Callback to register the page texture in the global array (returns IntPtr index)</param>
        public Atlas(VulkanContext vk, uint width, uint height, TextureFormat format,
                     IAtlasAllocator allocator, Func<Texture, IntPtr> registerTexture)
        {
            Width = width;
            Height = height;
            _allocator = allocator;
            _allocator.Reset((int)width, (int)height);

            Texture = Texture2D.CreateEmptyTexture(vk, new TextureData
            {
                Width = width,
                Height = height,
                Format = format,
                Dimension = TextureDimension.Texture2D,
                MipLevels = 1,
                ArrayLayers = 1,
                Usage = (TextureUsage)(ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit),
                GenerateMipmaps = false, ConvertToSrgb = false
            });

            TextureId = registerTexture(Texture);
        }

        /// <summary>
        /// Allocates a rectangular region. Throws if no space left.
        /// </summary>
        public AtlasRegion Allocate(int width, int height)
        {
            lock (_lock)
            {
                if (_allocator.Allocate(width, height, out Rect rect))
                {
                    return new AtlasRegion(this, rect, TextureId);
                }

                throw new InvalidOperationException("Atlas page is full.");
            }
        }

        /// <summary>
        /// Tries to allocate; returns false if full.
        /// </summary>
        public bool TryAllocate(int width, int height, out AtlasRegion? region)
        {
            lock (_lock)
            {
                if (_allocator.Allocate(width, height, out Rect rect))
                {
                    region = new AtlasRegion(this, rect, TextureId);
                    return true;
                }
                region = null;
                return false;
            }
        }

        /// <summary>
        /// Frees a previously allocated region.
        /// </summary>
        public void Free(AtlasRegion region)
        {
            if (region.Page != this)
            {
                throw new ArgumentException("Region does not belong to this atlas.", nameof(region));
            }

            lock (_lock)
            {
                _allocator.Free(region.Rect);
            }
        }

        /// <summary>
        /// Uploads raw RGBA8 pixel data into an allocated region.
        /// The region must have been allocated from this atlas.
        /// </summary>
        public void UploadPixels(UploadBatch batch, AtlasRegion region, ReadOnlySpan<byte> rgbaPixels)
        {
            if (region.Page != this)
            {
                throw new ArgumentException("Region does not belong to this atlas.");
            }

            // 1. Create a staging buffer large enough for the region
            ulong size = (ulong)rgbaPixels.Length;

            Texture.Image.TransitionImageLayout(batch, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
            batch.StageToImage(rgbaPixels, Texture.Image, 
                 ImageLayout.TransferDstOptimal,
                new Extent3D((uint)region.Rect.Width, (uint)region.Rect.Height, 1),
                new Offset3D(region.Rect.X, region.Rect.Y, 0),
                new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                });

            Texture.Image.TransitionImageLayout(batch, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

        }

        /// <summary>
        /// Copies an entire source texture (e.g., a loaded TextureAsset) into a previously allocated region.
        /// The source texture must have compatible dimensions and format.
        /// </summary>
        public unsafe void CopyFromTexture(UploadBatch batch, Texture source, AtlasRegion region)
        {
            if (region.Page != this)
            {
                throw new ArgumentException("Region does not belong to this atlas.");
            }

            var copyRegion = new ImageCopy
            {
                SrcSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                SrcOffset = new Offset3D(0, 0, 0),
                DstSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                DstOffset = new Offset3D(region.Rect.X, region.Rect.Y, 0),
                Extent = new Extent3D((uint)region.Rect.Width, (uint)region.Rect.Height, 1)
            };
            source.Image.TransitionImageLayout(batch,  ImageLayout.Undefined, ImageLayout.TransferSrcOptimal);
            Texture.Image.TransitionImageLayout(batch, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
            batch.CopyImage(source.Image, ImageLayout.TransferSrcOptimal, Texture.Image, ImageLayout.TransferDstOptimal, copyRegion);
            Texture.Image.TransitionImageLayout(batch, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);
        }

        public void Dispose()
        {
            Texture?.Dispose();
        }
    }

}
