using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

using Silk.NET.Maths;
using Silk.NET.Vulkan;

using SkiaSharp;

namespace RockEngine.Core.Rendering.Texturing
{
    public sealed partial class Texture2D : Texture
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

        public Texture2D(VulkanContext context, VkImage image, VkSampler sampler)
            : base(context, image, sampler) { }

        // Unified creation method using TextureData
        public static async Task<Texture2D> CreateAsync(VulkanContext context, TextureData textureData,
            CancellationToken cancellationToken = default)
        {
            if (!textureData.Validate())
            {
                throw new ArgumentException("Texture data is not valid", nameof(textureData));
            }
            return textureData.Dimension switch
            {
                TextureDimension.Texture2D => await Create2DAsync(context, textureData, cancellationToken).ConfigureAwait(false),
                TextureDimension.TextureCube => await CreateCubeAsync(context, textureData, cancellationToken).ConfigureAwait(false),
                /* TextureDimension.TextureArray => await CreateArrayAsync(context, textureData, cancellationToken),
                 TextureDimension.TextureCubeArray => await CreateCubeArrayAsync(context, textureData, cancellationToken),*/
                _ => throw new NotSupportedException($"Texture dimension {textureData.Dimension} not supported")
            };
        }

        // Create 2D texture
        private static async Task<Texture2D> Create2DAsync(VulkanContext context, TextureData textureData,
            CancellationToken cancellationToken)
        {
            if (!textureData.IsCubeMap)
            {
                textureData.MipLevels = textureData.CalculateMipLevels();
                textureData.GenerateMipmaps = true;
            }
            if (textureData.FilePaths.Count > 0)
            {
                return await CreateFromFileAsync(context, textureData, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                return CreateEmpty2D(context, textureData);
            }
        }

        // Create cube texture
        private static async Task<Texture2D> CreateCubeAsync(VulkanContext context, TextureData textureData,
            CancellationToken cancellationToken)
        {
            if (textureData.FilePaths.Count != 6)
            {
                throw new ArgumentException("Cube map requires exactly 6 file paths");
            }

            return await CreateCubeFromFilesAsync(context, textureData, cancellationToken).ConfigureAwait(false);
        }

        /*// Create array texture
        private static async Task<Texture2D> CreateArrayAsync(VulkanContext context, TextureData textureData,
            CancellationToken cancellationToken)
        {
            if (textureData.FilePaths.Count == 0)
                throw new ArgumentException("Array texture requires file paths");

            return await CreateArrayFromFilesAsync(context, textureData, cancellationToken);
        }

        // Create cube array texture
        private static async Task<Texture2D> CreateCubeArrayAsync(VulkanContext context, TextureData textureData,
            CancellationToken cancellationToken)
        {
            if (textureData.FilePaths.Count == 0 || textureData.FilePaths.Count % 6 != 0)
                throw new ArgumentException("Cube array texture requires multiple of 6 file paths");

            return await CreateCubeArrayFromFilesAsync(context, textureData, cancellationToken);
        }*/

        public static async Task<Texture2D> CreateAsync(VulkanContext context, string filePath,
            bool generateMipmaps = true, CancellationToken cancellationToken = default)
        {
            var textureData = new TextureData()
            {
                FilePaths = [filePath],
                GenerateMipmaps = generateMipmaps
            };

            return await CreateAsync(context, textureData, cancellationToken).ConfigureAwait(false);
        }


        // Create from file with TextureData
        private static async Task<Texture2D> CreateFromFileAsync(VulkanContext context, TextureData textureData,
            CancellationToken cancellationToken)
        {
            using var bitmap = SKBitmap.Decode(textureData.FilePaths[0]) ?? throw new InvalidOperationException($"Failed to decode image at path: {textureData.FilePaths[0]}");
            textureData.Width = (uint)bitmap.Width;
            textureData.Height = (uint)bitmap.Height;
            textureData.Format = TextureData.FromSKFormat(bitmap.ColorType, textureData.ConvertToSrgb); // also set format if missing
            return await CreateFromSkBitmapAsync(context, bitmap, textureData, cancellationToken).ConfigureAwait(false);
        }

        // Create from bytes with TextureData
        public static Texture2D CreateFromBytes(VulkanContext context, byte[] bytes, TextureData textureData)
        {
            var vkImage = CreateVulkanImage(context, textureData, ImageAspectFlags.ColorBit);

            // Create semaphores for queue synchronization
            var transferComplete = VkSemaphore.Create(context);
            var graphicsComplete = VkSemaphore.Create(context);


            // Transfer queue operations
            var transferBatch = context.TransferSubmitContext.CreateBatch();
            transferBatch.LabelObject(nameof(CreateFromBytes) + "Transfer");
            vkImage.TransitionImageLayout(
                transferBatch,
                ImageLayout.Undefined,
                ImageLayout.TransferDstOptimal,
                baseMipLevel: 0,
                levelCount: 1
            );
            CopyImageDataFromPointer(transferBatch, vkImage, bytes, textureData.Width, textureData.Height, textureData.GetVulkanFormat());

            transferBatch.AddSignalSemaphore(transferComplete);
            context.TransferSubmitContext.SubmitSingle(transferBatch, VkFence.CreateNotSignaled(context)).Wait();

            // Graphics queue operations
            var graphicsBatch = context.GraphicsSubmitContext.CreateBatch();
            graphicsBatch.LabelObject(nameof(CreateFromBytes) + "Graphics");
            graphicsBatch.AddWaitSemaphore(transferComplete, PipelineStageFlags.TransferBit);
            vkImage.TransitionImageLayout(
                graphicsBatch,
                ImageLayout.TransferDstOptimal,
                ImageLayout.ShaderReadOnlyOptimal
            );
            var graphicsFence = VkFence.CreateNotSignaled(context);
            var sampler = CreateSampler(context, vkImage.MipLevels);
            var texture = new Texture2D(context, vkImage, sampler);
            graphicsBatch.AddSignalSemaphore(texture.CompletionSemaphore);
            graphicsBatch.Submit();
            //context.GraphicsSubmitContext.SubmitSingle(graphicsBatch).Wait();

            return texture;
        }

        // Create from SKBitmap with TextureData
        private static async Task<Texture2D> CreateFromSkBitmapAsync(
         VulkanContext context,
         SKBitmap skBitmap,
         TextureData textureData,
         CancellationToken cancellationToken = default)
        {
            var format = textureData.GetVulkanFormat();
            uint mipLevels = textureData.EnsureMipLevels();   // 1 if no mips, else max possible
            textureData.MipLevels = mipLevels;                // ensure textureData is consistent

            // 1. Create image with the desired number of mip levels
            var image = CreateVulkanImage(context, textureData, ImageAspectFlags.ColorBit);

            // 2. Upload base level using transfer queue
            var transferComplete = VkSemaphore.Create(context);
            var transferBatch = context.TransferSubmitContext.CreateBatch();
            transferBatch.LabelObject("MipUpload");

            // Transition all levels to TransferDstOptimal so that later blits can write to them
            image.TransitionImageLayout(
                transferBatch,
                ImageLayout.Undefined,
                ImageLayout.TransferDstOptimal,
                baseMipLevel: 0,
                levelCount: mipLevels,
                baseArrayLayer: 0,
                layerCount: image.ArrayLayers);

            CopyImageData(transferBatch, skBitmap, image, format); // uploads to level 0
            transferBatch.AddSignalSemaphore(transferComplete);
            var transferOp = context.TransferSubmitContext.SubmitSingle(transferBatch);

            // 3. Graphics queue: generate mipmaps & final transition
            var graphicsBatch = context.GraphicsSubmitContext.CreateBatch();
            graphicsBatch.LabelObject("MipGeneration");
            graphicsBatch.AddWaitSemaphore(transferComplete, PipelineStageFlags.TransferBit);

            uint actualLoadedMipLevels = 1; // default: only level 0 has data
            bool mipGenerationSucceeded = false;

            if (textureData.GenerateMipmaps && mipLevels > 1)
            {
                // Transition level 0 from TransferDstOptimal to TransferSrcOptimal
                image.TransitionImageLayout(
                    graphicsBatch,
                    ImageLayout.TransferDstOptimal,
                    ImageLayout.TransferSrcOptimal,
                    baseMipLevel: 0,
                    levelCount: 1,
                    baseArrayLayer: 0,
                    layerCount: image.ArrayLayers);

                // Try blit-based generation; helper must handle internal transitions of lower levels
                mipGenerationSucceeded = image.GenerateMipmaps(graphicsBatch);
                actualLoadedMipLevels = mipGenerationSucceeded ? mipLevels : 1;
                // After mip generation
                if (mipGenerationSucceeded && mipLevels > 1)
                {
                    // Transition all source levels (0 .. mipLevels-2)
                    if (mipLevels > 1)
                    {
                        image.TransitionImageLayout(
                            graphicsBatch,
                            ImageLayout.TransferSrcOptimal,
                            ImageLayout.ShaderReadOnlyOptimal,
                            baseMipLevel: 0,
                            levelCount: mipLevels - 1,
                            baseArrayLayer: 0,
                            layerCount: image.ArrayLayers);
                    }

                    // Transition the final destination level (mipLevels-1)
                    image.TransitionImageLayout(
                        graphicsBatch,
                        ImageLayout.TransferDstOptimal,
                        ImageLayout.ShaderReadOnlyOptimal,
                        baseMipLevel: mipLevels - 1,
                        levelCount: 1,
                        baseArrayLayer: 0,
                        layerCount: image.ArrayLayers);

                    actualLoadedMipLevels = mipLevels;
                }
            }
            else
            {
                // Mipmaps not generated; only level 0 is valid and it's still in TransferDstOptimal.
                // Transition level 0 to ShaderReadOnlyOptimal.
                image.TransitionImageLayout(
                    graphicsBatch,
                    ImageLayout.TransferDstOptimal,
                    ImageLayout.ShaderReadOnlyOptimal,
                    baseMipLevel: 0,
                    levelCount: 1,
                    baseArrayLayer: 0,
                    layerCount: image.ArrayLayers);
                actualLoadedMipLevels = 1;
            }

            // 5. Create sampler with the actual number of loaded mip levels
            textureData.Sampler.SetMaxLod(actualLoadedMipLevels);
            var sampler = CreateSampler(context, textureData.Sampler, actualLoadedMipLevels);
            var texture = new Texture2D(context, image, sampler)
            {
                LoadedMipLevels = actualLoadedMipLevels
            };

            graphicsBatch.AddSignalSemaphore(texture.CompletionSemaphore);
            graphicsBatch.Submit();

            // Await the transfer queue submission to ensure staging buffer lifetime
            await transferOp;

            if (!string.IsNullOrEmpty(textureData.Name))
            {
                image.LabelObject(textureData.Name);
            }

            return texture;
        }

        // Create empty 2D texture
        private static Texture2D CreateEmpty2D(VulkanContext context, TextureData textureData)
        {
            var format = textureData.GetVulkanFormat();
            var usage = textureData.GetVulkanUsageFlags();

            // Add required usage flags for empty textures
            textureData.Usage |= TextureUsage.TransferDst | TextureUsage.Sampled;

            var image = CreateVulkanImage(context, textureData, ImageAspectFlags.ColorBit);

            // Transition to desired layout
            var batch = context.GraphicsSubmitContext.CreateBatch();
            image.TransitionImageLayout(
                batch,
                ImageLayout.Undefined,
                ImageLayout.ShaderReadOnlyOptimal,
                baseMipLevel: 0,
                levelCount: 1
            );
            var sampler = CreateSampler(context, textureData.Sampler, textureData.MipLevels);
            var texture = new Texture2D(context, image, sampler);
            batch.AddSignalSemaphore(texture.CompletionSemaphore);
            batch.Submit();
            //context.GraphicsSubmitContext.SubmitSingle(batch).Wait();

            if (!string.IsNullOrEmpty(textureData.Name))
            {
                image.LabelObject(textureData.Name);
            }

            return texture;
        }

        // Create cube from files
        private static async Task<Texture2D> CreateCubeFromFilesAsync(VulkanContext context, TextureData textureData,
            CancellationToken cancellationToken)
        {
            var faceBitmaps = new SKBitmap[6];
            await Parallel.ForAsync(0, 6, async (i, ct) =>
            {

                var bytes = await File.ReadAllBytesAsync(textureData.FilePaths[i], cancellationToken).ConfigureAwait(false);
                faceBitmaps[i] = SKBitmap.Decode(bytes);

                if (textureData.FlipVertically)
                {
                    faceBitmaps[i] = FlipBitmapVertically(faceBitmaps[i]);
                }
            }).ConfigureAwait(false);


            return await CreateCubeFromBitmapsAsync(context, faceBitmaps, textureData, cancellationToken).ConfigureAwait(false);
        }

        // Create cube from bitmaps
        private static async Task<Texture2D> CreateCubeFromBitmapsAsync(
    VulkanContext context,
    SKBitmap[] faceBitmaps,
    TextureData textureData,
    CancellationToken cancellationToken)
        {
            if (faceBitmaps.Length != 6)
            {
                throw new ArgumentException("Cube map requires exactly 6 face bitmaps");
            }

            uint width = (uint)faceBitmaps[0].Width;
            uint height = (uint)faceBitmaps[0].Height;
            uint mipLevels = textureData.EnsureMipLevels();
            textureData.Width = width;
            textureData.Height = height;
            textureData.Format = TextureData.FromSKFormat(faceBitmaps[0].ColorType, textureData.ConvertToSrgb);

            var image = CreateVulkanImage(context, textureData, ImageAspectFlags.ColorBit);

            // 1. Upload all 6 faces to level 0 using transfer queue
            var transferComplete = VkSemaphore.Create(context);
            var transferBatch = context.TransferSubmitContext.CreateBatch();
            transferBatch.LabelObject("CubeUpload");

            // Transition all layers/levels to TransferDstOptimal
            image.TransitionImageLayout(
                transferBatch,
                ImageLayout.Undefined,
                ImageLayout.TransferDstOptimal,
                baseMipLevel: 0,
                levelCount: mipLevels,
                baseArrayLayer: 0,
                layerCount: 6);

            for (int i = 0; i < 6; i++)
            {
                var pixelData = faceBitmaps[i].GetPixelSpan();
                if (!transferBatch.StagingManager.TryStage(transferBatch, pixelData,
                                                              out ulong bufferOffset,
                                                              out ulong stagedSize))
                {
                    throw new InvalidOperationException("Staging buffer overflow");
                }

                var bufferBarrier = new BufferMemoryBarrier2
                {
                    SType = StructureType.BufferMemoryBarrier2,
                    SrcStageMask = PipelineStageFlags2.HostBit,
                    DstStageMask = PipelineStageFlags2.TransferBit,
                    SrcAccessMask = AccessFlags2.HostWriteBit,
                    DstAccessMask = AccessFlags2.TransferReadBit,
                    Buffer = transferBatch.StagingManager.StagingBuffer,
                    Offset = bufferOffset,
                    Size = stagedSize
                };
                transferBatch.PipelineBarrier(bufferMemoryBarriers: [bufferBarrier]);

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
                transferBatch.CopyBufferToImage(
                    transferBatch.StagingManager.StagingBuffer,
                    image,
                    ImageLayout.TransferDstOptimal,
                    in copyRegion);
            }

            transferBatch.AddSignalSemaphore(transferComplete);
            transferBatch.Submit();
            await context.TransferSubmitContext.Submit();

            // 2. Graphics queue: mipmap generation & final layout
            var graphicsBatch = context.GraphicsSubmitContext.CreateBatch();
            graphicsBatch.LabelObject("CubeMipGeneration");
            graphicsBatch.AddWaitSemaphore(transferComplete, PipelineStageFlags.TransferBit);

            uint actualLoadedMipLevels = 1;
            bool mipGenerationSucceeded = false;

            if (textureData.GenerateMipmaps && mipLevels > 1)
            {
                // Level 0 is currently TransferDstOptimal (from the transfer batch)
                // Transition it to TransferSrcOptimal as required by GenerateMipmaps.
                image.TransitionImageLayout(
                    graphicsBatch,
                    ImageLayout.TransferDstOptimal,
                    ImageLayout.TransferSrcOptimal,
                    baseMipLevel: 0,
                    levelCount: 1,
                    baseArrayLayer: 0,
                    layerCount: 6);

                mipGenerationSucceeded = image.GenerateMipmaps(graphicsBatch);
            }

            // 3. Final transition: all valid levels to ShaderReadOnlyOptimal
            if (mipGenerationSucceeded)
            {
                // After GenerateMipmaps, all mips are either in TransferSrcOptimal (used as source)
                // or TransferDstOptimal (the highest mip, which was never used as source).
                // Transition the whole mip chain to ShaderReadOnlyOptimal.
                image.TransitionImageLayout(
                    graphicsBatch,
                    ImageLayout.TransferSrcOptimal,    // most levels are in this layout
                    ImageLayout.ShaderReadOnlyOptimal,
                    baseMipLevel: 0,
                    levelCount: mipLevels,
                    baseArrayLayer: 0,
                    layerCount: 6);
                actualLoadedMipLevels = mipLevels;
            }
            else
            {
                // No mipmaps – only level 0 is valid, still in TransferDstOptimal
                image.TransitionImageLayout(
                    graphicsBatch,
                    ImageLayout.TransferDstOptimal,
                    ImageLayout.ShaderReadOnlyOptimal,
                    baseMipLevel: 0,
                    levelCount: 1,
                    baseArrayLayer: 0,
                    layerCount: 6);
                actualLoadedMipLevels = 1;
            }

            var sampler = CreateSampler(context, textureData.Sampler, actualLoadedMipLevels);
            var texture = new Texture2D(context, image, sampler)
            {
                LoadedMipLevels = actualLoadedMipLevels
            };

            graphicsBatch.AddSignalSemaphore(texture.CompletionSemaphore);

            await graphicsBatch.SubmitContext.SubmitSingle(graphicsBatch);

            foreach (var bitmap in faceBitmaps)
            {
                bitmap.Dispose();
            }

            return texture;
        }

        private static SKBitmap FlipBitmapVertically(SKBitmap bitmap)
        {
            var flipped = new SKBitmap(bitmap.Width, bitmap.Height, bitmap.ColorType, bitmap.AlphaType);
            using var canvas = new SKCanvas(flipped);
            canvas.Scale(1, -1, bitmap.Width / 2f, bitmap.Height / 2f);
            canvas.DrawBitmap(bitmap, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            return flipped;
        }

        private static VkImage CreateVulkanImage(VulkanContext context, TextureData textureData, ImageAspectFlags aspectFlags)
        {
            var imageType = textureData.Dimension switch
            {
                TextureDimension.Texture3D => ImageType.Type3D,
                _ => ImageType.Type2D
            };

            var flags = ImageCreateFlags.None;
            if (textureData.IsCubeMap)
            {
                flags |= ImageCreateFlags.CreateCubeCompatibleBit;
            }

            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = imageType,
                Format = textureData.GetVulkanFormat(),
                Extent = new Extent3D(textureData.Width, textureData.Height, textureData.Depth),
                MipLevels = textureData.EnsureMipLevels(),
                ArrayLayers = textureData.ArrayLayers,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = textureData.GetVulkanUsageFlags(),
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined,
                Flags = flags
            };

            return VkImage.Create(context, imageInfo, textureData.GetVulkanMemoryFlags(), aspectFlags);
        }

        private static VkSampler CreateSampler(VulkanContext context, SamplerState samplerState, uint mipLevels)
        {
            var samplerCreateInfo = new SamplerCreateInfo
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = ConvertFilter(samplerState.MagFilter),
                MinFilter = ConvertFilter(samplerState.MinFilter),
                AddressModeU = ConvertWrap(samplerState.AddressModeU),
                AddressModeV = ConvertWrap(samplerState.AddressModeV),
                AddressModeW = ConvertWrap(samplerState.AddressModeW),
                AnisotropyEnable = context.Device.PhysicalDevice.Features.SamplerAnisotropy ? Vk.True : Vk.False,
                MaxAnisotropy = context.Device.PhysicalDevice.Properties.Limits.MaxSamplerAnisotropy,
                BorderColor = samplerState.BorderColor,
                UnnormalizedCoordinates = samplerState.UnnormalizedCoordinates,
                CompareEnable = samplerState.CompareEnable,
                CompareOp = samplerState.CompareOp,
                MipmapMode = /*samplerState.MipFilter == TextureFilter.Nearest ?
                    SamplerMipmapMode.Nearest : SamplerMipmapMode.Linear,*/SamplerMipmapMode.Linear,
                MipLodBias = samplerState.MipLodBias,
                MinLod = samplerState.MinLod,
                MaxLod = mipLevels
            };

            return context.SamplerCache.GetSampler(samplerCreateInfo);
        }

        private static Filter ConvertFilter(TextureFilter filter)
        {
            return filter switch
            {
                TextureFilter.Nearest => Filter.Nearest,
                TextureFilter.Linear => Filter.Linear,
                _ => Filter.Linear
            };
        }

        private static SamplerAddressMode ConvertWrap(TextureWrap wrap)
        {
            return wrap switch
            {
                TextureWrap.Repeat => SamplerAddressMode.Repeat,
                TextureWrap.MirroredRepeat => SamplerAddressMode.MirroredRepeat,
                TextureWrap.ClampToEdge => SamplerAddressMode.ClampToEdge,
                TextureWrap.ClampToBorder => SamplerAddressMode.ClampToBorder,
                _ => SamplerAddressMode.Repeat
            };
        }


        private static void CopyImageData(UploadBatch batch,
            SKBitmap skBitmap, VkImage vkImage, Format format, uint arrayLayer = 0)
        {
            CopyImageDataFromPointer(batch, vkImage, skBitmap.GetPixelSpan(),
                (uint)skBitmap.Width, (uint)skBitmap.Height, format, arrayLayer);
        }


        private static void CopyImageDataFromPointer(UploadBatch batch, VkImage vkImage,
            ReadOnlySpan<byte> data, uint width, uint height, Format format, uint arrayLayer = 0)
        {
            var imageSize = (ulong)(width * height * GetBytesPerPixel(format));

            // Stage data
            if (!batch.StagingManager.TryStage<byte>(batch, data, out var offset, out var size))
            {
                throw new Exception("Failed to stage texture data");
            }

            // Copy buffer to image
            var copyRegion = new BufferImageCopy
            {
                BufferOffset = offset,
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = vkImage.AspectFlags,
                    MipLevel = 0,
                    BaseArrayLayer = arrayLayer,
                    LayerCount = 1
                },
                ImageExtent = new Extent3D(width, height, 1)
            };

            batch.CopyBufferToImage(
                srcBuffer: batch.StagingManager.StagingBuffer,
                dstImage: vkImage,
                dstImageLayout: ImageLayout.TransferDstOptimal,
                pRegions: in copyRegion
            );
            var bufferBarrier = new BufferMemoryBarrier2
            {
                SType = StructureType.BufferMemoryBarrier2,
                SrcStageMask = PipelineStageFlags2.HostBit,
                DstStageMask = PipelineStageFlags2.TransferBit,
                SrcAccessMask = AccessFlags2.HostWriteBit,
                DstAccessMask = AccessFlags2.TransferReadBit,
                Buffer = batch.StagingManager.StagingBuffer,
                Offset = offset,
                Size = size
            };
            batch.PipelineBarrier(bufferMemoryBarriers: [bufferBarrier]);
        }

        private static uint GetBytesPerPixel(Format format)
        {
            return format switch
            {
                Format.R8G8B8A8Unorm => 4,
                Format.R8G8B8A8Srgb => 4,
                Format.B8G8R8A8Unorm => 4,
                Format.B8G8R8A8Srgb => 4,
                Format.R32G32B32A32Sfloat => 16,
                Format.D32Sfloat => 4,
                Format.D24UnormS8Uint => 4,
                _ => 4
            };
        }

        private static new uint CalculateMipLevels(uint width, uint height)
        {
            uint maxDimension = Math.Max(width, height);
            return (uint)Math.Floor(Math.Log2(maxDimension)) + 1;
        }

        // Static texture getters remain the same

        public static Texture2D GetEmptyTexture(VulkanContext context)
        {
            _emptyTexture ??= CreateColorTexture(context, new Vector4D<byte>(128, 128, 128, 255));
            return _emptyTexture;
        }

        public static Texture2D CreateEmptyTexture(VulkanContext context, TextureData textureData)
        {
            using var surface = SKSurface.Create(new SKImageInfo((int)textureData.Width, (int)textureData.Height, SKColorType.Rgba8888));
            surface.Canvas.Clear(new SKColor(0, 0, 0, 255));
            using var image = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);

            return LoadFromSKImage(context, bitmap, textureData, name: textureData.Name);
        }

        public static Texture2D CreateColorTexture(VulkanContext context, Vector4D<byte> color, string? name = null, uint mipLevels = 1)
        {
            using var surface = SKSurface.Create(new SKImageInfo(1, 1, SKColorType.Rgba8888));
            surface.Canvas.Clear(new SKColor(color.X, color.Y, color.Z, color.W));
            using var image = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);
            var texData = new TextureData
            {
                Height = (uint)bitmap.Height,
                Width = (uint)bitmap.Width,
                MipLevels = mipLevels,
                Name = name
            };
            return LoadFromSKImage(context, bitmap, texData, name: name);
        }


        public static Texture2D LoadFromSKImage(VulkanContext context, SKBitmap skImage, TextureData? textureData = null, string? name = null)
        {
            var width = (uint)skImage.Width;
            var height = (uint)skImage.Height;
            var format = GetVulkanFormat(skImage.Info.ColorType, context);
            textureData ??= new TextureData();
            textureData.Usage |= TextureUsage.TransferDst | TextureUsage.Sampled;
            textureData.Width = width;
            textureData.Height = height;

            var vkImage = CreateVulkanImage(context, textureData, ImageAspectFlags.ColorBit);


            var transferComplete = VkSemaphore.Create(context);
            var graphicsComplete = VkSemaphore.Create(context);

            var transferBatch = context.TransferSubmitContext.CreateBatch();
            vkImage.TransitionImageLayout(
               transferBatch,
               ImageLayout.Undefined,
               ImageLayout.TransferDstOptimal,
               baseMipLevel: 0,
               levelCount: 1
           );
            CopyImageDataFromPointer(transferBatch, vkImage, skImage.GetPixelSpan(),
                                    width, height, format);
            transferBatch.AddSignalSemaphore(transferComplete);
            context.TransferSubmitContext.SubmitSingle(transferBatch, VkFence.CreateNotSignaled(context)).Wait();
            var graphicsBatch = context.GraphicsSubmitContext.CreateBatch();
            graphicsBatch.AddWaitSemaphore(transferComplete, PipelineStageFlags.TransferBit);
            var sampler = CreateSampler(context, textureData.Sampler, textureData.MipLevels);

            var texture = new Texture2D(context, vkImage, sampler);

            vkImage.TransitionImageLayout(
                graphicsBatch,
                ImageLayout.TransferDstOptimal,
                ImageLayout.ShaderReadOnlyOptimal);
            graphicsBatch.AddSignalSemaphore(texture.CompletionSemaphore);
            graphicsBatch.Submit();

            if (!string.IsNullOrEmpty(name))
            {
                vkImage.LabelObject(name);
            }

            skImage.Dispose();
            return texture;
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
                CompareOp = CompareOp.LessOrEqual,
                MipmapMode = SamplerMipmapMode.Linear,
                MipLodBias = 0,
                MinLod = 0,
                MaxLod = 1
            };

            var sampler = context.SamplerCache.GetSampler(samplerCreateInfo);

            // Transition ALL layers to shader read-only optimal
            var batch = context.GraphicsSubmitContext.CreateBatch();
            image.TransitionImageLayout(
                batch,
                 ImageLayout.Undefined,
                ImageLayout.ShaderReadOnlyOptimal,
                baseMipLevel: 0,
                levelCount: 1,
                baseArrayLayer: 0,
                layerCount: arrayLayers  // Transition ALL layers
            );
            var texture = new Texture2D(context, image, sampler);
            batch.AddSignalSemaphore(texture.CompletionSemaphore);

            batch.Submit();

            if (!string.IsNullOrEmpty(name))
            {
                image.LabelObject(name);
            }

            return texture;
        }

        public static Texture2D CreatePointShadowMapArray(VulkanContext context, uint size,
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
                CompareOp = CompareOp.LessOrEqual,
                MipmapMode = SamplerMipmapMode.Linear,
                MipLodBias = 0,
                MinLod = 0,
                MaxLod = 1
            };

            var sampler = context.SamplerCache.GetSampler(samplerCreateInfo);

            // Transition ALL layers to shader read-only optimal
            var batch = context.GraphicsSubmitContext.CreateBatch();
            image.TransitionImageLayout(
                batch,
                ImageLayout.Undefined,
                ImageLayout.ShaderReadOnlyOptimal,
                baseMipLevel: 0,
                levelCount: 1,
                baseArrayLayer: 0,
                layerCount: 6 * arrayLayers  // Transition ALL layers, not just the first one
            );
            var texture = new Texture2D(context, image, sampler);
            batch.AddSignalSemaphore(texture.CompletionSemaphore);
            batch.Submit();

            if (!string.IsNullOrEmpty(name))
            {
                image.LabelObject(name);
            }

            return texture;
        }

        public Texture2D CopyWithNewUsage(PipelineManager pipelineManager, BindingManager bindingManager, ImageUsageFlags additionalUsage, Format newFormat = Format.R8G8B8A8Unorm)
        {
            // Compute the new usage flags
            var newUsage = Image.Usage | additionalUsage | ImageUsageFlags.StorageBit;

            // Create new image with same properties but new usage
            var newCreateInfo = Image.CreateInfo with
            {
                Usage = newUsage,
                Format = newFormat,
                InitialLayout = ImageLayout.Undefined,
            };

            var newImage = VkImage.Create(_context, newCreateInfo, MemoryPropertyFlags.DeviceLocalBit, Image.AspectFlags);
            newImage.LabelObject($"CopyOf_{Image.VkObjectNative}");
            var newTexture = new Texture2D(_context, newImage, _sampler);
            // Copy data using compute shader
            CopyViaComputeShader(pipelineManager, bindingManager, this, newTexture);
            return newTexture;

        }

        private void CopyViaComputeShader(PipelineManager pipelineManager, BindingManager bindingManager, Texture src, Texture dst)
        {
            var pipeline = pipelineManager.GetPipelineByName("ComputeCopyImage")
                ?? throw new InvalidOperationException("Missing pipeline: ComputeCopyImage");

            var batch = _context.ComputeSubmitContext.CreateBatch();
            batch.LabelObject(nameof(CopyViaComputeShader));

            src.Image.TransitionImageLayout(batch, ImageLayout.Undefined, ImageLayout.ShaderReadOnlyOptimal);
            dst.Image.TransitionImageLayout(batch, ImageLayout.Undefined, ImageLayout.General);
            batch.AddWaitSemaphore(src.CompletionSemaphore, PipelineStageFlags.ComputeShaderBit);


            for (uint layer = 0; layer < src.Image.ArrayLayers; layer++)
            {
                for (uint mip = 0; mip < src.Image.MipLevels; mip++)
                {
                    var textureBinding = new TextureBinding(
                        setLocation: 0,
                        bindingLocation: 0,
                        baseMipLevel: mip,
                        levelCount: 1,
                        imageLayout: ImageLayout.ShaderReadOnlyOptimal,
                        arrayLayer: layer,
                        layerCount: 1,
                        src);

                    var storageBinding = new StorageImageBinding(
                        texture: dst,
                        setLocation: 0,
                        bindingLocation: 1,
                        layout: ImageLayout.General,
                        mipLevel: mip,
                        arrayLayer: layer);


                    batch.BindPipeline(pipeline, PipelineBindPoint.Compute);
                    bindingManager.BindResource(0, batch, pipeline, true, textureBinding, storageBinding);

                    uint w = Math.Max(1, src.Image.Extent.Width >> (int)mip);
                    uint h = Math.Max(1, src.Image.Extent.Height >> (int)mip);
                    uint groupX = (w + 7) / 8;
                    uint groupY = (h + 7) / 8;
                    batch.Dispatch(groupX, groupY, 1);
                    batch.AddDependency(textureBinding);
                    batch.AddDependency(storageBinding);
                }
            }

            dst.Image.TransitionImageLayout(batch, ImageLayout.General, ImageLayout.ShaderReadOnlyOptimal);
            batch.AddSignalSemaphore(src.CompletionSemaphore);
            batch.AddSignalSemaphore(dst.CompletionSemaphore);
            batch.Submit();
            //_context.ComputeSubmitContext.SubmitSingle(batch, VkFence.CreateNotSignaled(_context)).Wait();
        }
    }
}