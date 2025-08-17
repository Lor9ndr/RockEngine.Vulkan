using RockEngine.Core.DI;
using RockEngine.Core.Helpers;
using RockEngine.Vulkan;

using Silk.NET.Maths;
using Silk.NET.Vulkan;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using SkiaSharp;

using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace RockEngine.Core.Rendering.Texturing
{
    public sealed class Texture2D : Texture
    {
        private static Texture2D? _emptyTexture;

        public uint Width => _image.Extent.Width;
        public uint Height => _image.Extent.Height;

        public Texture2D(VulkanContext context, VkImage image, VkImageView imageView,
                        VkSampler sampler, string? sourcePath = null)
            : base(context, image, imageView, sampler, sourcePath) { }

        public static async Task<Texture2D> CreateAsync(VulkanContext context, string filePath,
                                                       CancellationToken cancellationToken = default)
        {
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            var extension = Path.GetExtension(filePath);
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
            using (skBitmap)
            {
                var width = (uint)skBitmap.Width;
                var height = (uint)skBitmap.Height;
                var format = GetVulkanFormat(skBitmap.Info.ColorType, context);
                uint mipLevels = CalculateMipLevels(width, height);

                var image = CreateVulkanImage(context, width, height, format, mipLevels);
                var imageView = image.GetOrCreateView(ImageAspectFlags.ColorBit);
                UploadBatch batch = context.SubmitContext.CreateBatch();

                CopyImageData(context, batch, skBitmap, image, mipLevels > 1);

                if (mipLevels > 1)
                {
                    image.GenerateMipmaps(batch.CommandBuffer);
                }
                batch.Submit();
                //await context.SubmitContext.FlushSingle(batch, VkFence.CreateNotSignaled(context));

                image.LabelObject(Path.GetFileName(filePath));
                var sampler = CreateSampler(context, mipLevels);
                return new Texture2D(context, image, imageView, sampler, filePath);
            }
        }

        public static Texture2D Create(VulkanContext context, int width, int height,
                                      Format format, Span<byte> data)
        {
            var vkImage = CreateVulkanImage(context, (uint)width, (uint)height, format);
            var imageView = vkImage.GetOrCreateView(ImageAspectFlags.ColorBit);

            var batch = context.SubmitContext.CreateBatch();
            CopyImageDataFromPointer(context, batch, vkImage, data, (uint)width, (uint)height, format);
            batch.Submit();
            var sampler = CreateSampler(context, vkImage.MipLevels);
            return new Texture2D(context, vkImage, imageView, sampler);
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
                                               SKBitmap skBitmap, VkImage vkImage,
                                               bool keepTransferLayout = false)
        {
            CopyImageDataFromPointer(context, batch, vkImage, skBitmap.GetPixelSpan(),
                                    (uint)skBitmap.Width, (uint)skBitmap.Height,
                                    GetVulkanFormat(skBitmap.ColorType, context),
                                    keepTransferLayout);
        }

        private static unsafe void CopyImageDataFromPointer(
            VulkanContext context,
            UploadBatch batch,
            VkImage vkImage,
            Span<byte> data,
            uint width,
            uint height,
            Format format,
            bool keepTransferLayout = false)
        {
            var imageSize = (ulong)(width * height * format.GetBytesPerPixel());

            // 1. Переход в DST_OPTIMAL для копирования
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

            // 2. Копирование данных
            if (!context.SubmitContext.StagingManager.TryStage(batch, data, out var offset, out var size))
            {
                throw new Exception("Failed to stage data from image");
            }

            var bufferBarrier = new BufferMemoryBarrier
            {
                SType = StructureType.BufferMemoryBarrier,
                SrcAccessMask = AccessFlags.HostWriteBit,
                DstAccessMask = AccessFlags.TransferReadBit,
                Buffer = context.SubmitContext.StagingManager.StagingBuffer,
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
                srcBuffer: context.SubmitContext.StagingManager.StagingBuffer,
                dstImage: vkImage,
                dstImageLayout: ImageLayout.TransferDstOptimal,
                regionCount: 1,
                pRegions: &copyRegion
            );

            // 3. Переход в FINAL_LAYOUT (если не нужно сохранять DST)
            if (!keepTransferLayout)
            {
                var postCopyBarrier = new ImageMemoryBarrier
                {
                    SType = StructureType.ImageMemoryBarrier,
                    OldLayout = ImageLayout.TransferDstOptimal,
                    NewLayout = ImageLayout.ShaderReadOnlyOptimal,
                    Image = vkImage,
                    SubresourceRange = new ImageSubresourceRange
                    {
                        AspectMask = vkImage.AspectFlags,
                        BaseMipLevel = 0,
                        LevelCount = vkImage.MipLevels,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    },
                    SrcAccessMask = AccessFlags.TransferWriteBit,
                    DstAccessMask = AccessFlags.ShaderReadBit
                };

                batch.PipelineBarrier(
                    srcStage: PipelineStageFlags.TransferBit,
                    dstStage: PipelineStageFlags.FragmentShaderBit,
                    imageMemoryBarriers: new[] { postCopyBarrier }
                );
            }
        }

        public static Texture2D GetEmptyTexture(VulkanContext context)
        {
            _emptyTexture ??=  CreateColorTexture(context, new Vector4D<byte>(0, 0, 0, 1));
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
            var imageView = vkImage.GetOrCreateView(ImageAspectFlags.ColorBit);
            var batch = context.SubmitContext.CreateBatch();
            batch.CommandBuffer.LabelObject("LoadFromSKImage cmd");
            CopyImageDataFromPointer(context, batch, vkImage, skImage.GetPixelSpan(),
                                    width, height, format);

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
            skImage.Dispose();
            var texture =  new Texture2D(context, vkImage, imageView, sampler);
            texture.PrepareForFragmentShader(batch.CommandBuffer);
            batch.Submit();
            return texture;

        }
    }
}
