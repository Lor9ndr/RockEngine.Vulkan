using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

using SkiaSharp;

namespace RockEngine.Vulkan.VkObjects
{
    public class Texture : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly Image _image;
        private readonly DeviceMemory _imageMemory;
        private readonly ImageView _imageView;
        private readonly Sampler _sampler;

        public Image Image => _image;
        public ImageView ImageView => _imageView;
        public Sampler Sampler => _sampler;

        private Texture(VulkanContext context, Image vkImage, DeviceMemory imageMemory, ImageView imageView, Sampler sampler)
        {
            _context = context;
            _image = vkImage;
            _imageMemory = imageMemory;
            _imageView = imageView;
            _sampler = sampler;
        }

        public static async Task<Texture> FromFileAsync(VulkanContext context, string path, CancellationToken cancellationToken = default)
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken)
                .ConfigureAwait(false);
            using var skImage = SKImage.FromBitmap(SKBitmap.Decode(bytes));

            var width = (uint)skImage.Width;
            var height = (uint)skImage.Height;

            var format = GetVulkanFormat(skImage);
            var vkImage = CreateVulkanImage(context, width, height, format);
            var imageMemory = AllocateImageMemory(context, vkImage);
            var imageView = CreateImageView(context, vkImage, format);

            // Copy image data from SkiaSharp to Vulkan image
            CopyImageData(context, skImage, vkImage);

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
            var sampler = Sampler.Create(context, in samplerCreateInfo);

            return new Texture(context, vkImage, imageMemory, imageView, sampler);
        }

        private unsafe static Image CreateVulkanImage(VulkanContext context, uint width, uint height, Format format)
        {
            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = format,
                Extent = new Extent3D
                {
                    Width = width,
                    Height = height,
                    Depth = 1
                },
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined
            };

            return Image.Create(context, in imageInfo);
        }

        private static DeviceMemory AllocateImageMemory(VulkanContext context, Image vkImage)
        {
            context.Api.GetImageMemoryRequirements(context.Device, vkImage, out var memRequirements);

            return DeviceMemory.Allocate(context, memRequirements, MemoryPropertyFlags.DeviceLocalBit);
        }

        private unsafe static ImageView CreateImageView(VulkanContext context, Image vkImage, Format format)
        {
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = vkImage,
                ViewType = ImageViewType.Type2D,
                Format = format,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            return ImageView.Create(context, in viewInfo);
        }

        private static CommandBufferWrapper BeginSingleTimeCommands(VulkanContext context, CommandPoolWrapper commandPool)
        {
            var allocInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                Level = CommandBufferLevel.Primary,
                CommandPool = commandPool,
                CommandBufferCount = 1
            };
            var commandBuffer = CommandBufferWrapper.Create(context, in allocInfo, commandPool);

            commandBuffer.Begin(new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit
            });

            return commandBuffer;
        }

        private unsafe static void EndSingleTimeCommands(VulkanContext context, CommandBufferWrapper commandBuffer, CommandPoolWrapper commandPool)
        {
            var vk = context.Api;
            var device = context.Device;
            commandBuffer.End();

            var commandBufferNative = commandBuffer.VkObjectNative;
            var submitInfo = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &commandBufferNative
            };

            vk.QueueSubmit(context.Device.GraphicsQueue, 1, in submitInfo, default);
            vk.QueueWaitIdle(context.Device.GraphicsQueue);

            vk.FreeCommandBuffers(device, commandPool, 1, in commandBufferNative);
        }

        private static Format GetVulkanFormat(SKImage skImage)
        {
            // Determine the Vulkan format based on the SkiaSharp image color type
            return skImage.ColorType switch
            {
                SKColorType.Rgba8888 => Format.R8G8B8A8Unorm,
                SKColorType.Bgra8888 => Format.B8G8R8A8Unorm,
                _ => throw new NotSupportedException($"Unsupported color type: {skImage.ColorType}")
            };
        }

        private static void CopyImageData(VulkanContext context, SKImage skImage, Image vkImage)
        {
            var format = GetVulkanFormat(skImage);
            var width = (uint)skImage.Width;
            var height = (uint)skImage.Height;
            var imageSize = skImage.Info.BytesSize64;

            // Create a staging buffer
            BufferCreateInfo bci = new BufferCreateInfo()
            {
                SType = StructureType.BufferCreateInfo,
                Usage = BufferUsageFlags.TransferSrcBit,
                Size = (ulong)imageSize,
                SharingMode = SharingMode.Exclusive
            };
            using var stagingBuffer = BufferWrapper.Create(context, in bci, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            // Map the buffer and copy the image data
            stagingBuffer.MapMemory(out var data);
            skImage.PeekPixels().ReadPixels(new SKImageInfo((int)width, (int)height, skImage.ColorType, SKAlphaType.Premul), data, (int)(width * 4));
            stagingBuffer.UnmapMemory();

            context.QueueMutex.WaitOne();
            try
            {
                // Transition the Vulkan image layout to TRANSFER_DST_OPTIMAL
                TransitionImageLayout(context, vkImage, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);

                // Copy the data from the staging buffer to the Vulkan image
                CopyBufferToImage(context, stagingBuffer, vkImage, width, height);

                // Transition the Vulkan image layout to SHADER_READ_ONLY_OPTIMAL
                TransitionImageLayout(context, vkImage, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);
            }
            finally
            {
                context.QueueMutex.ReleaseMutex();
            }
        }

        private unsafe static void TransitionImageLayout(VulkanContext context, Image image, ImageLayout oldLayout, ImageLayout newLayout)
        {
            var commandPool = context.GetOrCreateCommandPool();
            using var commandBuffer = BeginSingleTimeCommands(context, commandPool);

            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = oldLayout,
                NewLayout = newLayout,
                Image = image,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            PipelineStageFlags srcStage;
            PipelineStageFlags dstStage;

            if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.TransferDstOptimal)
            {
                barrier.SrcAccessMask = 0;
                barrier.DstAccessMask = AccessFlags.TransferWriteBit;

                srcStage = PipelineStageFlags.TopOfPipeBit;
                dstStage = PipelineStageFlags.TransferBit;
            }
            else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
            {
                barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
                barrier.DstAccessMask = AccessFlags.ShaderReadBit;

                srcStage = PipelineStageFlags.TransferBit;
                dstStage = PipelineStageFlags.FragmentShaderBit;
            }
            else
            {
                throw new Exception("Unsupported layout transition");
            }

            context.Api.CmdPipelineBarrier(commandBuffer, srcStage, dstStage, 0, 0, null, 0, null, 1, &barrier);

            EndSingleTimeCommands(context, commandBuffer, commandPool);
        }

        private unsafe static void CopyBufferToImage(VulkanContext context, BufferWrapper stagingBuffer, Silk.NET.Vulkan.Image image, uint width, uint height)
        {
            var commandPool = context.GetOrCreateCommandPool();
            using var commandBuffer = BeginSingleTimeCommands(context, commandPool);

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

            context.Api.CmdCopyBufferToImage(commandBuffer, stagingBuffer, image, ImageLayout.TransferDstOptimal, 1, &region);

            EndSingleTimeCommands(context, commandBuffer, commandPool);
        }

        public unsafe void Dispose()
        {
            _sampler.Dispose();
            _imageView.Dispose();
            _image.Dispose();
            _imageMemory.Dispose();
        }
    }
}