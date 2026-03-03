using RockEngine.Assets;
using RockEngine.Core.Assets;
using RockEngine.Core.Builders;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Editor.EditorUI.Thumbnails
{
    public interface IThumbnailRenderer
    {
        Task<Thumbnail> RenderThumbnailAsync(IAsset asset, int size = 128, CancellationToken cancellationToken = default);
    }
    public class ThumbnailRenderer:IThumbnailRenderer
    {
        private readonly VulkanContext _context;
        private readonly PipelineManager _pipelineManager;
        private readonly BindingManager _bindingManager;

        public ThumbnailRenderer(VulkanContext context, ShaderManager shaderManager, PipelineManager pipelineManager, BindingManager bindingManager)
        {
            _context = context;
            _pipelineManager = pipelineManager;
            _bindingManager = bindingManager;
            var builder = new ComputePipelineBuilder(_context, "ComputeCopyImage");
            var shader = VkShaderModule.Create(_context, shaderManager.GetShader("ComputeCopyImage.comp"), ShaderStageFlags.ComputeBit);
            builder.WithShaderModule(shader);
            pipelineManager.Create(builder);
        }

        public async Task<Thumbnail> RenderThumbnailAsync(IAsset asset, int size = 128, CancellationToken cancellationToken = default)
        {
            if(asset is TextureAsset textureAsset)
            {
                if(textureAsset.Texture is null)
                {
                    await textureAsset.LoadGpuResourcesAsync();
                }
                if(textureAsset.Texture is Texture2D texture2D)
                {
                    var texture = await CreateTextureThumbnail(texture2D, _pipelineManager, _bindingManager);
                    var thumbnail = new Thumbnail(asset, size, size, texture);
                    return thumbnail;
                }
            }
            throw new NotImplementedException();
        }
        public async Task<Texture2D> CreateTextureThumbnail(Texture2D sourceTexture, PipelineManager pipelineManager, BindingManager bindingManager, uint size = 128)
        {
            // Ensure source has TransferSrc usage (may create a copy)
            if (!sourceTexture.Image.Usage.HasFlag(ImageUsageFlags.TransferSrcBit))
            {
                sourceTexture = sourceTexture.CopyWithNewUsage(pipelineManager, bindingManager,
                    ImageUsageFlags.TransferSrcBit);
            }

            // Determine if this is a cube map
            bool isCube = sourceTexture.Image.ArrayLayers == 6 &&
                          sourceTexture.Image.CreateInfo.Flags.HasFlag(ImageCreateFlags.CreateCubeCompatibleBit);

            // Create the thumbnail image (always 2D, single layer)
            var thumbnailImage = VkImage.Create(
                _context,
                size, size,
                Format.R8G8B8A8Unorm,
                ImageTiling.Optimal,
                ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
                MemoryPropertyFlags.DeviceLocalBit,
                ImageLayout.Undefined,
                1, 1, SampleCountFlags.Count1Bit,
                ImageAspectFlags.ColorBit);
            thumbnailImage.LabelObject("ThumbnailTexture");

            var batch = _context.GraphicsSubmitContext.CreateBatch();
            batch.LabelObject("CreateTextureThumbnail");

            // Transition source to TransferSrcOptimal (all layers)
            sourceTexture.Image.TransitionImageLayout(batch,
                ImageLayout.Undefined,
                ImageLayout.TransferSrcOptimal,
                baseMipLevel: 0,
                levelCount: 1,
                baseArrayLayer: 0,
                layerCount: sourceTexture.Image.ArrayLayers);

            // Transition thumbnail to TransferDstOptimal
            thumbnailImage.TransitionImageLayout(batch,
                ImageLayout.Undefined,
                ImageLayout.TransferDstOptimal);

            if (isCube && size >= 3)
            {
                uint cellSize = size / 3; // each face will occupy cellSize x cellSize pixels
                uint cols = 3;
                uint rows = 2;

                for (uint layer = 0; layer < 6; layer++)
                {
                    uint col = layer % cols;
                    uint row = layer / cols;

                    int dstX = (int)(col * cellSize);
                    int dstY = (int)(row * cellSize);
                    int dstW = (int)cellSize;
                    int dstH = (int)cellSize;

                    var blitRegion = new ImageBlit
                    {
                        SrcSubresource = new ImageSubresourceLayers(
                            ImageAspectFlags.ColorBit,
                            0,      // mip level
                            layer,  // base array layer
                            1),     // layer count
                        SrcOffsets = new ImageBlit.SrcOffsetsBuffer
                        {
                            [0] = new Offset3D(0, 0, 0),
                            [1] = new Offset3D(
                                (int)sourceTexture.Image.Extent.Width,
                                (int)sourceTexture.Image.Extent.Height,
                                1)
                        },
                        DstSubresource = new ImageSubresourceLayers(
                            ImageAspectFlags.ColorBit,
                            0,      // mip level
                            0,      // base array layer (thumbnail has only one layer)
                            1),     // layer count
                        DstOffsets = new ImageBlit.DstOffsetsBuffer
                        {
                            [0] = new Offset3D(dstX, dstY, 0),
                            [1] = new Offset3D(dstX + dstW, dstY + dstH, 1)
                        }
                    };

                    batch.BlitImage(
                        sourceTexture.Image, ImageLayout.TransferSrcOptimal,
                        thumbnailImage, ImageLayout.TransferDstOptimal,
                        in blitRegion, Filter.Linear);
                }
            }
            else
            {
                // Fallback: copy only the first layer (original behavior for 2D textures)
                var blitRegion = new ImageBlit
                {
                    SrcSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                    SrcOffsets = new ImageBlit.SrcOffsetsBuffer
                    {
                        [0] = new Offset3D(0, 0, 0),
                        [1] = new Offset3D((int)sourceTexture.Image.Extent.Width,
                                           (int)sourceTexture.Image.Extent.Height, 1)
                    },
                    DstSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                    DstOffsets = new ImageBlit.DstOffsetsBuffer
                    {
                        [0] = new Offset3D(0, 0, 0),
                        [1] = new Offset3D((int)size, (int)size, 1)
                    }
                };
                batch.BlitImage(
                    sourceTexture.Image, ImageLayout.TransferSrcOptimal,
                    thumbnailImage, ImageLayout.TransferDstOptimal,
                    in blitRegion, Filter.Linear);
            }

            // Transition thumbnail to shader‑readable layout
            thumbnailImage.TransitionImageLayout(batch,
                ImageLayout.TransferDstOptimal,
                ImageLayout.ShaderReadOnlyOptimal);

            // Transition source back to ShaderReadOnlyOptimal (optional, good practice)
            sourceTexture.Image.TransitionImageLayout(batch,
                ImageLayout.TransferSrcOptimal,
                ImageLayout.ShaderReadOnlyOptimal,
                baseMipLevel: 0,
                levelCount: 1,
                baseArrayLayer: 0,
                layerCount: sourceTexture.Image.ArrayLayers);
            // Create sampler and wrap in Texture2D
            var samplerCreateInfo = new SamplerCreateInfo
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Linear,
                MinFilter = Filter.Linear,
                MipmapMode = SamplerMipmapMode.Linear,
                AddressModeU = SamplerAddressMode.Repeat,
                AddressModeV = SamplerAddressMode.Repeat,
                AddressModeW = SamplerAddressMode.Repeat,
                MinLod = 0,
                MaxLod = 0
            };
            var sampler = _context.SamplerCache.GetSampler(samplerCreateInfo);

            var texture = new Texture2D(_context, thumbnailImage, sampler);
            batch.AddSignalSemaphore(texture.CompletionSemaphore);
            await _context.GraphicsSubmitContext.SubmitSingle(batch);
            return texture;
        }
    }
}