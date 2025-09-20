using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using SkiaSharp;

namespace RockEngine.Core.Rendering.Texturing
{
    /// <summary>
    /// Represents a Vulkan texture resource, managing image, image view, and sampler lifecycle.
    /// Handles 2D textures, cubemaps, and provides texture creation utilities.
    /// </summary>
    // Texture base class
    public abstract partial class Texture : IDisposable
    {
        // Vulkan context for resource management
        protected readonly VulkanContext _context;

        // Vulkan image resources
        protected VkImage _image;
        protected VkSampler _sampler;

        // Resource management flags
        private bool _disposed;
        private uint _loadedMipLevels;

        /// <summary>
        /// Vulkan image resource
        /// </summary>
        public VkImage Image => _image;

        /// <summary>
        /// Number of loaded mipmap levels
        /// </summary>
        public uint LoadedMipLevels { get => _loadedMipLevels; protected set => _loadedMipLevels = value; }

        /// <summary>
        /// Total available mipmap levels
        /// </summary>
        public uint TotalMipLevels => _image.MipLevels;
        public Guid ID { get; } = Guid.NewGuid();
        public string? SourcePath { get; }
        public bool IsDisposed => _disposed;

        public bool IsFullyLoaded => LoadedMipLevels >= TotalMipLevels;

        public event Action<Texture>? OnTextureUpdated;

        protected Texture(VulkanContext context, VkImage image, VkSampler sampler, string? sourcePath = null)
        {
            _context = context;
            _image = image;
            _sampler = sampler;
            SourcePath = sourcePath;
            LoadedMipLevels = 1;
            Image.OnImageResized += (img) => NotifyTextureUpdated();
        }

        protected void NotifyTextureUpdated()
        {
            OnTextureUpdated?.Invoke(this);
        }

        public void PrepareForComputeShader(VkCommandBuffer commandBuffer)
        {
            _image.TransitionImageLayout(
                commandBuffer,
                ImageLayout.General,
                baseMipLevel: 0,
                levelCount: LoadedMipLevels,
                baseArrayLayer: 0,
                layerCount: _image.ArrayLayers);
        }

        public void PrepareForFragmentShader(VkCommandBuffer commandBuffer)
        {
            _image.TransitionImageLayout(
                commandBuffer,
                ImageLayout.ShaderReadOnlyOptimal,
                baseMipLevel: 0,
                levelCount: LoadedMipLevels,
                baseArrayLayer: 0,
                layerCount: _image.ArrayLayers);
        }

        public static VkSampler CreateSampler(VulkanContext context, uint mipLevels)
        {
            var samplerCreateInfo = new SamplerCreateInfo
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Linear,
                MinFilter = Filter.Linear, // Use linear for minification with mipmaps
                MipmapMode = SamplerMipmapMode.Linear, // Linear interpolation between mip levels
                AddressModeU = SamplerAddressMode.Repeat,
                AddressModeV = SamplerAddressMode.Repeat,
                AddressModeW = SamplerAddressMode.Repeat,
                MipLodBias = 0.0f,
                AnisotropyEnable = Vk.True,
                MaxAnisotropy = context.Device.PhysicalDevice.Properties.Limits.MaxSamplerAnisotropy,
                CompareEnable = Vk.False,
                CompareOp = CompareOp.Always,
                MinLod = 0.0f,
                MaxLod = mipLevels - 1, // Important: Set to actual available mip levels
                BorderColor = BorderColor.IntOpaqueBlack,
                UnnormalizedCoordinates = Vk.False
            };

            return context.SamplerCache.GetSampler(samplerCreateInfo);
        }

        protected static uint CalculateMipLevels(uint width, uint height)
        {
            return (uint)Math.Floor(Math.Log(Math.Max(width, height), 2)) + 1;
        }

        protected static Format GetVulkanFormat(SKColorType colorType, VulkanContext context)
        {
            var features = context.Device.PhysicalDevice.GetPhysicalDeviceFeatures();

            if (features.TextureCompressionBC && colorType == SKColorType.Rgba8888)
            {
                return Format.BC3UnormBlock;
            }

            return colorType switch
            {
                SKColorType.Rgba8888 => Format.R8G8B8A8Unorm,
                SKColorType.Bgra8888 => Format.B8G8R8A8Unorm,
                SKColorType.Gray8 => Format.R8Unorm,
                SKColorType.RgbaF32 => Format.R32G32B32A32Sfloat,
                _ => throw new NotSupportedException($"Unsupported color type: {colorType}")
            };
        }

        public virtual void Dispose()
        {
            if (!_disposed)
            {
                _image?.Dispose();
                _disposed = true;
            }
        }
    }
}
