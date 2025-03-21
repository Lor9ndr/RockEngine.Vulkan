﻿using SkiaSharp;
using Silk.NET.Vulkan;
using RockEngine.Vulkan;

namespace RockEngine.Core
{
    public class Texture : IDisposable
    {
        private readonly RenderingContext _context;
        private readonly VkImage _image;
        private readonly VkImageView _imageView;
        private readonly VkSampler _sampler;
        private bool _disposed;
        private static Texture? _emptyTexture;

        public VkImageView ImageView => _imageView;
        public VkSampler Sampler => _sampler;
        public VkImage Image => _image;

        public Texture(RenderingContext context, VkImage image, VkImageView imageView, VkSampler sampler)
        {
            _context = context;
            _image = image;
            _imageView = imageView;
            _sampler = sampler;
        }
       
        public static async Task<Texture> CreateAsync(RenderingContext context, string filePath, VkCommandBuffer? commandBuffer = null, CancellationToken cancellationToken = default)
        {
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken)
                .ConfigureAwait(false);
            using var skBitmap = SKBitmap.Decode(bytes);

            var width = (uint)skBitmap.Width;
            var height = (uint)skBitmap.Height;

            var format = GetVulkanFormat(skBitmap.Info.ColorType);
            var image = CreateVulkanImage(context, width, height, format);
            var imageView = CreateImageView(context, image, format);

            // Copy image data from SkiaSharp to Vulkan image
            CopyImageData(context, skBitmap, image, commandBuffer);

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
            return new Texture(context,image, imageView, sampler);
        }

        public unsafe static Texture Create(RenderingContext context, int width, int height, Format format, nint data, VkCommandBuffer vkCommandBuffer = null)
        {
            // Create the Vulkan image
            var vkImage = CreateVulkanImage(context, (uint)width, (uint)height, format, ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit | ImageUsageFlags.ColorAttachmentBit);

            // Create the image view
            var imageView = CreateImageView(context, vkImage, format);
            // Copy image data from the pointer to the Vulkan image
            CopyImageDataFromPointer(context, vkImage, data.ToPointer(), (uint)width, (uint)height, format, vkCommandBuffer);

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

            return new Texture(context, vkImage, imageView, sampler);
        }

        private static VkImage CreateVulkanImage(
            RenderingContext context, 
            uint width, 
            uint height, 
            Format format, 
            ImageUsageFlags imageUsageFlags = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit)
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
                Usage = imageUsageFlags,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined
            };

            return VkImage.Create(context, in imageInfo, MemoryPropertyFlags.DeviceLocalBit);
        }

        private static VkImageView CreateImageView(RenderingContext context, VkImage vkImage, Format format)
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

            return VkImageView.Create(context,vkImage, in viewInfo);
        }

        private static Format GetVulkanFormat(SKColorType colorType)
        {
            return colorType switch
            {
                SKColorType.Rgba8888 => Format.R8G8B8A8Unorm,
                SKColorType.Bgra8888 => Format.B8G8R8A8Unorm,
                SKColorType.Gray8 => Format.R8Unorm,
                SKColorType.RgbaF32 => Format.R32G32B32A32Sfloat,
                _ => throw new NotSupportedException($"Unsupported color type: {colorType}")
            };
        }
        private static int GetBytesPerPixel(Format format)
        {
            return format switch
            {
                Format.R8G8B8A8Unorm => 4,
                Format.B8G8R8A8Unorm => 4,
                Format.R8Unorm => 1,
                Format.R32G32B32A32Sfloat => 16,
                _ => throw new NotSupportedException($"Unsupported format: {format}")
            };
        }

        private unsafe static void CopyImageData(RenderingContext context, SKBitmap skBitmap, VkImage vkImage, VkCommandBuffer? commandBuffer = null)
        {
            CopyImageDataFromPointer(context,vkImage, skBitmap.GetPixels().ToPointer(), (uint)skBitmap.Width, (uint)skBitmap.Height, GetVulkanFormat(skBitmap.ColorType), commandBuffer);
        }


        private unsafe static void CopyImageDataFromPointer(RenderingContext context, VkImage vkImage, void* data, uint width, uint height, Format format, VkCommandBuffer? commandBuffer = null)
        {
            if (data == null)
            {
                // Handle the case where data is null
                // For example, you might want to initialize the image with a default color or skip the copy
                return;
            }
            var imageSize = (ulong)(width * height * GetBytesPerPixel(format));

            // Create a staging buffer
            using var stagingBuffer = VkBuffer.CreateAndCopyToStagingBuffer(context, data, imageSize);

            VkCommandPool? cmdPool = null;
            if (commandBuffer is null)
            {
                /// 
                cmdPool = VkCommandPool.Create(context, CommandPoolCreateFlags.TransientBit, context.Device.QueueFamilyIndices.GraphicsFamily.Value);
                commandBuffer = cmdPool.AllocateCommandBuffer();
            }
            using var fence = VkFence.CreateNotSignaled(context);
            var fenceNative = fence.VkObjectNative;
            commandBuffer.BeginSingleTimeCommand();
            // Transition the Vulkan image layout to TRANSFER_DST_OPTIMAL
            vkImage.TransitionImageLayout(commandBuffer, format, ImageLayout.TransferDstOptimal);

            // Copy the data from the staging buffer to the Vulkan image
            CopyBufferToImage(commandBuffer, stagingBuffer, vkImage, width, height);

            // Transition the Vulkan image layout to SHADER_READ_ONLY_OPTIMAL
            vkImage.TransitionImageLayout(commandBuffer, format, ImageLayout.ShaderReadOnlyOptimal);
            commandBuffer.End();

            var realBuffer = commandBuffer.VkObjectNative;
            var submitInfo = new SubmitInfo();
            submitInfo.SType = StructureType.SubmitInfo;
            submitInfo.CommandBufferCount = 1;
            submitInfo.PCommandBuffers = &realBuffer;
            context.Device.GraphicsQueue.Submit(in submitInfo, fence);
            RenderingContext.Vk.WaitForFences(context.Device, 1, in fenceNative, true, ulong.MaxValue);
            cmdPool?.Dispose();
        }


        private unsafe static void CopyBufferToImage(VkCommandBuffer commandBuffer, VkBuffer stagingBuffer, VkImage image, uint width, uint height)
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

            RenderingContext.Vk.CmdCopyBufferToImage(commandBuffer, stagingBuffer, image, ImageLayout.TransferDstOptimal, 1, &region);
        }



        public static Texture GetEmptyTexture(RenderingContext context)
        {
            _emptyTexture ??= CreateEmptyTexture(context);
            return _emptyTexture;
        }

        private static Texture CreateEmptyTexture(RenderingContext context)
        {
            // Create a 1x1 pink texture
            using var surface = SKSurface.Create(new SKImageInfo(1, 1, SKColorType.Rgba8888));
            surface.Canvas.Clear(SKColors.Pink);
            using var image = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);
            return LoadFromSKImage(context, bitmap);
        }

        public static Texture LoadFromSKImage(RenderingContext context, SKBitmap skImage)
        {
            var width = (uint)skImage.Width;
            var height = (uint)skImage.Height;
            var format = GetVulkanFormat(skImage.Info.ColorType);
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
            return new Texture(context, vkImage, imageView, sampler);
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

        public DescriptorImageInfo GetDescriptorInfo()
        {
            return new DescriptorImageInfo()
            {
                ImageLayout = _image.CurrentLayout,
                ImageView = _imageView,
                Sampler = _sampler,
            };
        }
      
    }

}
