using RockEngine.Assets;
using RockEngine.Core.Assets;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Core.Rendering.Texturing.Atlasing;
using RockEngine.Editor.EditorUI.ImGuiRendering;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Editor.EditorUI.Thumbnails
{
    public interface IThumbnailRenderer
    {
        Task<Thumbnail> RenderThumbnailAsync(IAsset asset, int size = 128, CancellationToken cancellationToken = default);
    }

    public class ThumbnailRenderer : IThumbnailRenderer
    {
        private readonly VulkanContext _context;
        private readonly Atlas _thumbAtlas;          // The atlas for packing thumbnails

        public ThumbnailRenderer(
            VulkanContext context,
            ImGuiController controller)
        {
            _context = context;

            // Create a 4096×4096 atlas with 256×256 fixed cells
            var allocator = new GridAllocator();
            allocator.Configure(256, 256);
            _thumbAtlas = new Atlas(
                context,
                width: 4096, height: 4096,
                format: TextureFormat.R8G8B8A8Unorm,
                allocator: allocator,
                registerTexture: controller.GetTextureID
            );
        }

        public async Task<Thumbnail> RenderThumbnailAsync(IAsset asset, int size = 256, CancellationToken cancellationToken = default)
        {
            if (asset is not TextureAsset textureAsset)
            {
                throw new NotSupportedException($"Thumbnail rendering not supported for {asset.GetType().Name}");
            }

            // Ensure GPU texture is loaded
            if (textureAsset.Texture is null)
            {
                await textureAsset.LoadGpuResourcesAsync().ConfigureAwait(false);
            }

            if (textureAsset.Texture is not Texture2D sourceTexture)
            {
                throw new InvalidOperationException("Unsupported texture type");
            }

            // Use the atlas cell size (256), ignore the size parameter to match the GridAllocator
            const int cellSize = 256;

            // Allocate a region in the atlas
            if (!_thumbAtlas.TryAllocate(cellSize, cellSize, out var region))
            {
                throw new InvalidOperationException("Thumbnail atlas is full.");
            }

            // Blit the source texture into the allocated atlas region
            BlitSourceToAtlas(sourceTexture, region);

            // Return the thumbnail with the atlas region
            return new Thumbnail(asset, cellSize, cellSize, region);
        }

        /// <summary>
        /// Blits the source texture (2D or cube‑map) into the specified atlas region.
        /// </summary>
        private void BlitSourceToAtlas(Texture2D sourceTexture, AtlasRegion region)
        {
            var batch = _context.GraphicsSubmitContext.CreateBatch();

            // ---------- transition source to TransferSrcOptimal ----------
            sourceTexture.Image.TransitionImageLayout(
                batch,
                ImageLayout.Undefined,
                ImageLayout.TransferSrcOptimal,
                baseMipLevel: 0,
                levelCount: 1,
                baseArrayLayer: 0,
                layerCount: sourceTexture.Image.ArrayLayers);

            // ---------- transition atlas to TransferDstOptimal ----------
            _thumbAtlas.Texture.Image.TransitionImageLayout(
                batch,
                ImageLayout.Undefined,
                ImageLayout.TransferDstOptimal);

            bool isCube = sourceTexture.Image.ArrayLayers == 6 &&
                          sourceTexture.Image.CreateInfo.Flags.HasFlag(ImageCreateFlags.CreateCubeCompatibleBit);

            if (isCube)
            {
                // Blit cube faces into a 3×2 grid inside the 256×256 cell
                uint cols = 3;
                uint faceCellW = (uint)region.Rect.Width / cols;   // 85
                uint faceCellH = (uint)region.Rect.Height / 2;     // 128

                for (uint layer = 0; layer < 6; layer++)
                {
                    uint col = layer % cols;
                    uint row = layer / cols;

                    var blitRegion = new ImageBlit
                    {
                        SrcSubresource = new ImageSubresourceLayers(
                            ImageAspectFlags.ColorBit, 0, layer, 1),
                        SrcOffsets = new ImageBlit.SrcOffsetsBuffer
                        {
                            [0] = new Offset3D(0, 0, 0),
                            [1] = new Offset3D(
                                (int)sourceTexture.Image.Extent.Width,
                                (int)sourceTexture.Image.Extent.Height,
                                1)
                        },
                        DstSubresource = new ImageSubresourceLayers(
                            ImageAspectFlags.ColorBit, 0, 0, 1),
                        DstOffsets = new ImageBlit.DstOffsetsBuffer
                        {
                            [0] = new Offset3D(
                                region.Rect.X + (int)(col * faceCellW),
                                region.Rect.Y + (int)(row * faceCellH),
                                0),
                            [1] = new Offset3D(
                                region.Rect.X + (int)((col + 1) * faceCellW),
                                region.Rect.Y + (int)((row + 1) * faceCellH),
                                1)
                        }
                    };

                    batch.BlitImage(
                        sourceTexture.Image, ImageLayout.TransferSrcOptimal,
                        _thumbAtlas.Texture.Image, ImageLayout.TransferDstOptimal,
                        in blitRegion, Filter.Linear);
                }
            }
            else
            {
                // Single face (2D texture)
                var blitRegion = new ImageBlit
                {
                    SrcSubresource = new ImageSubresourceLayers(
                        ImageAspectFlags.ColorBit, 0, 0, 1),
                    SrcOffsets = new ImageBlit.SrcOffsetsBuffer
                    {
                        [0] = new Offset3D(0, 0, 0),
                        [1] = new Offset3D(
                            (int)sourceTexture.Image.Extent.Width,
                            (int)sourceTexture.Image.Extent.Height,
                            1)
                    },
                    DstSubresource = new ImageSubresourceLayers(
                        ImageAspectFlags.ColorBit, 0, 0, 1),
                    DstOffsets = new ImageBlit.DstOffsetsBuffer
                    {
                        [0] = new Offset3D(region.Rect.X, region.Rect.Y, 0),
                        [1] = new Offset3D(
                            region.Rect.X + region.Rect.Width,
                            region.Rect.Y + region.Rect.Height,
                            1)
                    }
                };

                batch.BlitImage(
                    sourceTexture.Image, ImageLayout.TransferSrcOptimal,
                    _thumbAtlas.Texture.Image, ImageLayout.TransferDstOptimal,
                    in blitRegion, Filter.Linear);
            }

            // ---------- transition atlas back to ShaderReadOnlyOptimal ----------
            _thumbAtlas.Texture.Image.TransitionImageLayout(
                batch,
                ImageLayout.TransferDstOptimal,
                ImageLayout.ShaderReadOnlyOptimal);

            // ---------- restore source layout (optional) ----------
            sourceTexture.Image.TransitionImageLayout(
                batch,
                ImageLayout.TransferSrcOptimal,
                ImageLayout.ShaderReadOnlyOptimal,
                baseMipLevel: 0,
                levelCount: 1,
                baseArrayLayer: 0,
                layerCount: sourceTexture.Image.ArrayLayers);

            batch.Submit();  // ensures the blit is executed on the GPU
        }
    }
}