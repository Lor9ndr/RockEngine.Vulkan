using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects.Infos.Texture;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

using SkiaSharp;

using System.Diagnostics;

namespace RockEngine.Vulkan.VkObjects
{
    public class Texture : IDisposable
    {
        private static Texture _emptyTexture;

        public TextureInfo TextureInfo { get; private set;}

        public Texture(string path)
        {
            TextureInfo = new NotLoadedTextureInfo(path);
        }

        public Texture(TextureInfo info)
        {
            TextureInfo = info;
        }

        public async Task LoadAsync(VulkanContext context, CancellationToken cancellationToken = default)
        {
            if (TextureInfo is not NotLoadedTextureInfo preLoadInfo)
            {
                Debugger.Log(1, "Texture loading", "Texture is already loaded");
                return;
            }

            var bytes = await File.ReadAllBytesAsync(preLoadInfo.Path, cancellationToken)
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

            TextureInfo = new LoadedTextureInfo(vkImage, imageMemory, imageView, sampler);
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

            return Image.Create(context, in imageInfo, MemoryPropertyFlags.DeviceLocalBit);
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

        private static Format GetVulkanFormat(SKImage skImage)
        {
            // Determine the Vulkan format based on the SkiaSharp image color type
            return skImage.ColorType switch
            {
                SKColorType.Rgba8888 => Format.R8G8B8A8Unorm,
                SKColorType.Bgra8888 => Format.B8G8R8A8Unorm,
                SKColorType.Gray8 => Format.R8Unorm,
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

            // Transition the Vulkan image layout to TRANSFER_DST_OPTIMAL
            vkImage.TransitionImageLayout(context, format, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);

            // Copy the data from the staging buffer to the Vulkan image
            CopyBufferToImage(context, stagingBuffer, vkImage, width, height);

            // Transition the Vulkan image layout to SHADER_READ_ONLY_OPTIMAL
            vkImage.TransitionImageLayout(context, format, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);
        }

        private unsafe static void CopyBufferToImage(VulkanContext context, BufferWrapper stagingBuffer, Image image, uint width, uint height)
        {
            var commandPool = context.GetOrCreateCommandPool();
            using var commandBuffer = VkHelper.BeginSingleTimeCommands(context, commandPool);

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

            VkHelper.EndSingleTimeCommands(context, commandBuffer, commandPool);
        }

        public static Texture GetEmptyTexture(VulkanContext context)
        {
            _emptyTexture ??= CreateEmptyTexture(context);
            return _emptyTexture;
        }

        private static Texture CreateEmptyTexture(VulkanContext context)
        {
            // Create a 1x1 white texture
            using var surface = SKSurface.Create(new SKImageInfo(1, 1, SKColorType.Rgba8888));
            surface.Canvas.Clear(SKColors.White);
            using var image = surface.Snapshot();
            var texture = new Texture(new NotLoadedTextureInfo("empty_texture"));
            texture.LoadFromSKImage(context, image);
            return texture;
        }
        private void LoadFromSKImage(VulkanContext context, SKImage skImage)
        {
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

            TextureInfo = new LoadedTextureInfo(vkImage, imageMemory, imageView, sampler);
        }

        public unsafe void Dispose()
        {
            if (TextureInfo is not LoadedTextureInfo loaded)
            {
                return;
            }
            loaded.Sampler.Dispose();
            loaded.ImageView.Dispose();
            loaded.Image.Dispose();
            loaded.ImageMemory.Dispose();
        }
    }
}
