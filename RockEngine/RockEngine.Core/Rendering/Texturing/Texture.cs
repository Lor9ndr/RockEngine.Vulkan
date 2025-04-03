using RockEngine.Core.Helpers;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using SkiaSharp;

namespace RockEngine.Core.Rendering.Texturing
{
    public class Texture : IDisposable
    {
        protected readonly VulkanContext _context;
        protected VkImage _image;
        protected VkImageView _imageView;
        protected VkSampler _sampler;
        private bool _disposed;
        private static Texture? _emptyTexture;
        private uint _loadedMipLevels;

        public VkImageView ImageView => _imageView;
        public VkSampler Sampler => _sampler;
        public VkImage Image => _image;

        public uint LoadedMipLevels { get => _loadedMipLevels; protected set => _loadedMipLevels = value; }
        public uint TotalMipLevels => _image.MipLevels;

        public uint Width => _image.Width;
        public uint Height => _image.Height;

        public Guid ID { get; } = Guid.NewGuid();
        public string? SourcePath { get; }

        public event Action<Texture>? OnTextureUpdated;

        public Texture(VulkanContext context, VkImage image, VkImageView imageView, VkSampler sampler, string? sourcePath)
        {
            _context = context;
            _image = image;
            _imageView = imageView;
            _sampler = sampler;
            SourcePath = sourcePath;
            LoadedMipLevels = 1;
        }

        public static async Task<Texture> CreateAsync(VulkanContext context, string filePath, CancellationToken cancellationToken = default)
        {
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
            using var skBitmap = SKBitmap.Decode(bytes);

            var width = (uint)skBitmap.Width;
            var height = (uint)skBitmap.Height;
            var format = GetVulkanFormat(skBitmap.Info.ColorType, context);
            uint mipLevels = CalculateMipLevels(width, height);

            var image = CreateVulkanImage(context, width, height, format,
                ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
                mipLevels);

            var imageView = CreateImageView(context, image, format);

            // Copy initial data and leave in TRANSFER_DST_OPTIMAL if generating mipmaps
            CopyImageData(context, skBitmap, image, mipLevels > 1);

            if (mipLevels > 1)
                GenerateMipmaps(context, image, format, width, height);

            var sampler = CreateSampler(context, mipLevels);
            return new Texture(context, image, imageView, sampler, filePath);
        }

        protected void NotifyTextureUpdated()
        {
            OnTextureUpdated?.Invoke(this);
        }


        public static unsafe Texture Create(VulkanContext context, int width, int height, Format format, nint data)
        {
            var vkImage = CreateVulkanImage(context, (uint)width, (uint)height, format);
            var imageView = CreateImageView(context, vkImage, format);

            CopyImageDataFromPointer(context, vkImage, data.ToPointer(), (uint)width, (uint)height, format);

            var sampler = CreateSampler(context, vkImage.MipLevels);
            return new Texture(context, vkImage, imageView, sampler, null);
        }

        private static VkImage CreateVulkanImage(
         VulkanContext context,
         uint width,
         uint height,
         Format format,
         ImageUsageFlags usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
         uint mipLevels = 1)
        {
            // Validate compressed texture dimensions
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
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined
            };

            return VkImage.Create(context, imageInfo, MemoryPropertyFlags.DeviceLocalBit);
        }

        public static VkImageView CreateImageView(VulkanContext context, VkImage image, Format format)
        {
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = image,
                ViewType = ImageViewType.Type2D,
                Format = format,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = image.MipLevels,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            return VkImageView.Create(context, image, viewInfo);
        }

        protected static VkSampler CreateSampler(VulkanContext context, uint mipLevels)
        {
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
                MaxLod = mipLevels
            };

            return context.SamplerCache.GetSampler(samplerCreateInfo);
        }
        public static async Task<Texture> CreateBaseImageAsync(VulkanContext context, string texturePath)
        {
            using var bitmap = SKBitmap.Decode(texturePath);
            uint width = (uint)bitmap.Width;
            uint height = (uint)bitmap.Height;
            uint mipLevels = CalculateMipLevels(width, height);
            var format = GetVulkanFormat(bitmap.ColorType, context);

            var image = VkImage.Create(
                context,
                 new ImageCreateInfo()
                 {
                     Extent = new Extent3D(width, height, 1),
                     Format = format,
                     Tiling = ImageTiling.Optimal,
                     Usage = ImageUsageFlags.SampledBit |
                         ImageUsageFlags.TransferDstBit |
                         ImageUsageFlags.TransferSrcBit,
                     SType = StructureType.ImageCreateInfo,
                     Samples = SampleCountFlags.Count1Bit,
                     ImageType = ImageType.Type2D,
                     ArrayLayers = 1,
                     MipLevels = mipLevels,
                     InitialLayout = ImageLayout.Undefined,
                 },
                MemoryPropertyFlags.DeviceLocalBit);

            // Копирование данных и генерация мипмапов
            using var staging = await VkBuffer.CreateAndCopyToStagingBuffer<byte>(
                context,
                bitmap.GetPixelSpan().ToArray(),
                (ulong)bitmap.ByteCount);

            unsafe
            {
                context.SubmitSingleTimeCommand(cmd =>
                {
                    image.TransitionImageLayout(cmd, format, ImageLayout.TransferDstOptimal);

                    var copy = new BufferImageCopy
                    {
                        ImageSubresource = new ImageSubresourceLayers
                        {
                            AspectMask = ImageAspectFlags.ColorBit,
                            MipLevel = 0,
                            BaseArrayLayer = 0,
                            LayerCount = 1,
                        },
                        ImageExtent = new Extent3D(width, height, 1)
                    };

                    VulkanContext.Vk.CmdCopyBufferToImage(cmd, staging, image,
                        ImageLayout.TransferDstOptimal, 1, &copy);

                    image.GenerateMipmaps(cmd, format);
                });

                return new Texture(context, image,
                    image.CreateView(ImageAspectFlags.ColorBit, mipLevels, 1),
                    CreateSampler(context, mipLevels), texturePath);
            }

        }

        private static uint CalculateMipLevels(uint width, uint height)
        {
            return (uint)Math.Floor(Math.Log(Math.Max(width, height), 2)) + 1;
        }


        private static unsafe void GenerateMipmaps(VulkanContext context, VkImage image, Format format, uint width, uint height)
        {
            var vk = VulkanContext.Vk;
            var physicalDevice = context.Device.PhysicalDevice;
            vk.GetPhysicalDeviceFormatProperties(physicalDevice, format, out var formatProperties);

            if ((formatProperties.OptimalTilingFeatures & FormatFeatureFlags.SampledImageFilterLinearBit) == 0)
                throw new NotSupportedException($"Texture format {format} doesn't support linear blitting!");

            var mipLevels = image.MipLevels;
            var cmdBuffer = BeginTemporaryCommandBuffer(context);

            try
            {
                for (uint i = 1; i < mipLevels; i++)
                {
                    var blit = new ImageBlit
                    {
                        SrcSubresource = new ImageSubresourceLayers
                        {
                            AspectMask = ImageAspectFlags.ColorBit,
                            MipLevel = i - 1,
                            BaseArrayLayer = 0,
                            LayerCount = 1
                        },
                        SrcOffsets = new ImageBlit.SrcOffsetsBuffer
                        {
                            Element0 = new Offset3D(0, 0, 0),
                            Element1 = new Offset3D(
                                (int)Math.Max(width >> (int)(i - 1), 1),
                                (int)Math.Max(height >> (int)(i - 1), 1),
                                1)
                        },
                        DstSubresource = new ImageSubresourceLayers
                        {
                            AspectMask = ImageAspectFlags.ColorBit,
                            MipLevel = i,
                            BaseArrayLayer = 0,
                            LayerCount = 1
                        },
                        DstOffsets = new ImageBlit.DstOffsetsBuffer
                        {
                            Element0 = new Offset3D(0, 0, 0),
                            Element1 = new Offset3D(
                                (int)Math.Max(width >> (int)i, 1),
                                (int)Math.Max(height >> (int)i, 1),
                                1)
                        }
                    };

                    // Prepare source mip level
                    var srcBarrier = new ImageMemoryBarrier
                    {
                        SType = StructureType.ImageMemoryBarrier,
                        Image = image,
                        SubresourceRange = new ImageSubresourceRange
                        {
                            AspectMask = ImageAspectFlags.ColorBit,
                            BaseMipLevel = i - 1,
                            LevelCount = 1,
                            BaseArrayLayer = 0,
                            LayerCount = 1
                        },
                        OldLayout = ImageLayout.TransferDstOptimal,
                        NewLayout = ImageLayout.TransferSrcOptimal,
                        SrcAccessMask = AccessFlags.TransferWriteBit,
                        DstAccessMask = AccessFlags.TransferReadBit
                    };

                    vk.CmdPipelineBarrier(cmdBuffer,
                        PipelineStageFlags.TransferBit,
                        PipelineStageFlags.TransferBit,
                        0, 0, null, 0, null, 1, &srcBarrier);

                    // Blit between mip levels
                    vk.CmdBlitImage(cmdBuffer,
                        image, ImageLayout.TransferSrcOptimal,
                        image, ImageLayout.TransferDstOptimal,
                        1, &blit, Filter.Linear);

                    // Transition source mip to shader read
                    var srcReadBarrier = new ImageMemoryBarrier
                    {
                        SType = StructureType.ImageMemoryBarrier,
                        Image = image,
                        SubresourceRange = new ImageSubresourceRange
                        {
                            AspectMask = ImageAspectFlags.ColorBit,
                            BaseMipLevel = i - 1,
                            LevelCount = 1,
                            BaseArrayLayer = 0,
                            LayerCount = 1
                        },
                        OldLayout = ImageLayout.TransferSrcOptimal,
                        NewLayout = ImageLayout.ShaderReadOnlyOptimal,
                        SrcAccessMask = AccessFlags.TransferReadBit,
                        DstAccessMask = AccessFlags.ShaderReadBit
                    };

                    vk.CmdPipelineBarrier(cmdBuffer,
                        PipelineStageFlags.TransferBit,
                        PipelineStageFlags.FragmentShaderBit,
                        0, 0, null, 0, null, 1, &srcReadBarrier);
                }

                // Final transition for last mip level
                var finalBarrier = new ImageMemoryBarrier
                {
                    SType = StructureType.ImageMemoryBarrier,
                    Image = image,
                    SubresourceRange = new ImageSubresourceRange
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        BaseMipLevel = mipLevels - 1,
                        LevelCount = 1,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    },
                    OldLayout = ImageLayout.TransferDstOptimal,
                    NewLayout = ImageLayout.ShaderReadOnlyOptimal,
                    SrcAccessMask = AccessFlags.TransferWriteBit,
                    DstAccessMask = AccessFlags.ShaderReadBit
                };

                vk.CmdPipelineBarrier(cmdBuffer,
                    PipelineStageFlags.TransferBit,
                    PipelineStageFlags.FragmentShaderBit,
                    0, 0, null, 0, null, 1, &finalBarrier);

                image.SetCurrentLayout(ImageLayout.ShaderReadOnlyOptimal);
            }
            finally
            {
                EndTemporaryCommandBuffer(context, cmdBuffer);
            }
        }
        private static VkCommandBuffer BeginTemporaryCommandBuffer(VulkanContext context)
        {
            var cmdPool = VkCommandPool.Create(context, CommandPoolCreateFlags.TransientBit, context.Device.QueueFamilyIndices.GraphicsFamily.Value);
            var cmdBuffer = cmdPool.AllocateCommandBuffer();
            cmdBuffer.BeginSingleTimeCommand();
            return cmdBuffer;
        }

        private static unsafe void EndTemporaryCommandBuffer(VulkanContext context, VkCommandBuffer cmdBuffer)
        {
            cmdBuffer.End();
            using var fence = VkFence.CreateNotSignaled(context);
            var buffer = cmdBuffer.VkObjectNative;
            var submitInfo = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &buffer
            };
            context.Device.GraphicsQueue.Submit(submitInfo, fence);
            fence.Wait();
            cmdBuffer.Dispose();
        }


        private static Format GetVulkanFormat(SKColorType colorType, VulkanContext context)
        {
            var features = context.Device.PhysicalDevice.GetPhysicalDeviceFeatures();

            // Check for BC compression support
            if (features.TextureCompressionBC && colorType == SKColorType.Rgba8888)
            {
                return Format.BC3UnormBlock;
            }

            // Fallback to existing formats
            return colorType switch
            {
                SKColorType.Rgba8888 => Format.R8G8B8A8Unorm,
                SKColorType.Bgra8888 => Format.B8G8R8A8Unorm,
                SKColorType.Gray8 => Format.R8Unorm,
                SKColorType.RgbaF32 => Format.R32G32B32A32Sfloat,
                _ => throw new NotSupportedException($"Unsupported color type: {colorType}")
            };
        }


        private static unsafe void CopyImageData(VulkanContext context, SKBitmap skBitmap, VkImage vkImage, bool keepTransferLayout = false)
        {
            CopyImageDataFromPointer(context, vkImage, skBitmap.GetPixels().ToPointer(), (uint)skBitmap.Width, (uint)skBitmap.Height, GetVulkanFormat(skBitmap.ColorType, context), keepTransferLayout);
        }


        private static unsafe void CopyImageDataFromPointer(
             VulkanContext context,
             VkImage vkImage,
             void* data,
             uint width,
             uint height,
             Format format,
             bool keepTransferLayout = false)
        {
            if (data == null)
            {
                // Handle the case where data is null
                // For example, you might want to initialize the image with a default color or skip the copy
                return;
            }
            var imageSize = (ulong)(width * height * format.GetBytesPerPixel());

            // Create a staging buffer
            using var stagingBuffer = VkBuffer.CreateAndCopyToStagingBuffer(context, data, imageSize);

            VkCommandPool? cmdPool = null;
            cmdPool = VkCommandPool.Create(context, CommandPoolCreateFlags.TransientBit, context.Device.QueueFamilyIndices.GraphicsFamily.Value);
            var commandBuffer = cmdPool.AllocateCommandBuffer();
            commandBuffer.BeginSingleTimeCommand();

            using var fence = VkFence.CreateNotSignaled(context);
            var fenceNative = fence.VkObjectNative;
            // Transition the Vulkan image layout to TRANSFER_DST_OPTIMAL
            vkImage.TransitionImageLayout(commandBuffer, format, ImageLayout.TransferDstOptimal);

            // Copy the data from the staging buffer to the Vulkan image
            CopyBufferToImage(commandBuffer, stagingBuffer, vkImage, width, height);

            // Transition the Vulkan image layout to SHADER_READ_ONLY_OPTIMAL
            if (!keepTransferLayout)
            {
                vkImage.TransitionImageLayout(commandBuffer, format, ImageLayout.ShaderReadOnlyOptimal);
            }
            commandBuffer.End();

            var realBuffer = commandBuffer.VkObjectNative;
            var submitInfo = new SubmitInfo();
            submitInfo.SType = StructureType.SubmitInfo;
            submitInfo.CommandBufferCount = 1;
            submitInfo.PCommandBuffers = &realBuffer;
            context.Device.GraphicsQueue.Submit(in submitInfo, fence);
            VulkanContext.Vk.WaitForFences(context.Device, 1, in fenceNative, true, ulong.MaxValue);
            cmdPool?.Dispose();
        }


        private static unsafe void CopyBufferToImage(VkCommandBuffer commandBuffer, VkBuffer stagingBuffer, VkImage image, uint width, uint height)
        {
            var region = new BufferImageCopy
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
                ImageOffset = new Offset3D { X = 0, Y = 0, Z = 0 },
                ImageExtent = new Extent3D { Width = width, Height = height, Depth = 1 }
            };

            VulkanContext.Vk.CmdCopyBufferToImage(commandBuffer, stagingBuffer, image, ImageLayout.TransferDstOptimal, 1, &region);
        }



        public static Texture GetEmptyTexture(VulkanContext context)
        {
            _emptyTexture ??= CreateEmptyTexture(context);
            return _emptyTexture;
        }

        private static Texture CreateEmptyTexture(VulkanContext context)
        {
            // Create a 1x1 pink texture
            using var surface = SKSurface.Create(new SKImageInfo(1, 1, SKColorType.Rgba8888));
            surface.Canvas.Clear(SKColors.Pink);
            using var image = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);
            return LoadFromSKImage(context, bitmap);
        }

        public static Texture LoadFromSKImage(VulkanContext context, SKBitmap skImage)
        {
            var width = (uint)skImage.Width;
            var height = (uint)skImage.Height;
            var format = GetVulkanFormat(skImage.Info.ColorType, context);
            var vkImage = CreateVulkanImage(context, width, height, format);
            var imageView = CreateImageView(context, vkImage, format);

            unsafe
            {
                // Copy image data from SkiaSharp to Vulkan image
                CopyImageDataFromPointer(context, vkImage, skImage.GetPixels().ToPointer(), width, height, format);
            }

            // Create a sampler
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
            var sampler = VkSampler.Create(context, in samplerCreateInfo);
            skImage.Dispose();
            return new Texture(context, vkImage, imageView, sampler, null);
        }


        public void Dispose()
        {
            if (!_disposed)
            {
                _imageView.Dispose();
                _image.Dispose();
                _disposed = true;
            }
        }

    }
    public static class FormatExtensions
    {
        public static bool IsBlockCompressed(this Format format)
        {
            return format.ToString().StartsWith("BC");
        }

        public static (int Width, int Height) GetBlockSize(this Format format)
        {
            return format.ToString() switch
            {
                string s when s.StartsWith("BC") => (4, 4),
                _ => (1, 1)
            };
        }
    }

}
