using RockEngine.Vulkan;
using Silk.NET.Vulkan;
using SkiaSharp;

namespace RockEngine.Core.Rendering.Texturing
{
    public abstract partial class Texture : IDisposable, IDescriptorInfoProvider<DescriptorImageInfo>, IResourceTrackable
    {
        protected readonly VulkanContext _context;
        protected VkImage _image;
        protected VkSampler _sampler;
        protected VkSemaphore _completionSemaphore;

        private bool _disposed;
        private uint _loadedMipLevels;

        public VkImage Image => _image;
        public uint LoadedMipLevels { get => _loadedMipLevels; protected set => _loadedMipLevels = value; }

        private readonly ImageObserver _imageObserver;

        public uint TotalMipLevels => _image.MipLevels;
        public string? SourcePath { get; }
        public bool IsDisposed => _disposed;
        public bool IsFullyLoaded => LoadedMipLevels >= TotalMipLevels;
        public VkSemaphore CompletionSemaphore => _completionSemaphore;

        private readonly ResourceTracker _tracker = new ResourceTracker();
        public ulong ID => _tracker.ID;
        public IDisposable Subscribe(IResourceObserver observer) => _tracker.Subscribe(observer);

        protected void NotifyObservers(ResourceChangeType type) => _tracker.NotifyObservers(type);
        protected void OnDataUpdated() => NotifyObservers(ResourceChangeType.DataUpdated);
        protected void OnResized() => NotifyObservers(ResourceChangeType.Resized);

        // Внутренний наблюдатель за изменениями VkImage (ресайз)
        private class ImageObserver(Texture texture) : IResourceObserver
        {
            public void OnResourceChanged(ulong resourceId, ResourceChangeType changeType)
            {
                if (changeType == ResourceChangeType.Resized)
                {
                    texture.OnResized();
                }
            }
        }

        protected Texture(VulkanContext context, VkImage image, VkSampler sampler)
        {
            _context = context;
            _image = image;
            _sampler = sampler;
            LoadedMipLevels = 1;

            // Подписка на изменения изображения (ресайз) через новый механизм
            _imageObserver = new ImageObserver(this);
            _image.Subscribe(_imageObserver);

            _completionSemaphore = VkSemaphore.Create(context);
            _completionSemaphore.LabelObject($"CompletionSemaphore of Image {image.VkObjectNative}");
        }

        public void PrepareForComputeShader(UploadBatch batch)
        {
            _image.TransitionImageLayout(
                batch,
                ImageLayout.Undefined,
                ImageLayout.General,
                baseMipLevel: 0,
                levelCount: LoadedMipLevels,
                baseArrayLayer: 0,
                layerCount: _image.ArrayLayers);
        }

        public void PrepareForFragmentShader(UploadBatch batch)
        {
            _image.TransitionImageLayout(
                batch,
                ImageLayout.Undefined,
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
                MinFilter = Filter.Linear,
                MipmapMode = SamplerMipmapMode.Linear,
                AddressModeU = SamplerAddressMode.Repeat,
                AddressModeV = SamplerAddressMode.Repeat,
                AddressModeW = SamplerAddressMode.Repeat,
                MipLodBias = 0.0f,
                AnisotropyEnable = Vk.False,
                MaxAnisotropy = context.Device.PhysicalDevice.Properties.Limits.MaxSamplerAnisotropy,
                CompareEnable = Vk.False,
                CompareOp = CompareOp.Always,
                MinLod = 0.0f,
                MaxLod = mipLevels - 1,
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
                // Уведомление об удалении должно быть обёрнуто в DeferredOperation
                _context.GraphicsSubmitContext.AddDependency(new DeferredOperation(() =>
                {
                    NotifyObservers(ResourceChangeType.Disposed);
                    _tracker.Clear(); // чистим подписчиков
                }));

                _context.GraphicsSubmitContext.AddDependency(_completionSemaphore);
                _context.GraphicsSubmitContext.AddDependency(_image);
                _context.GraphicsSubmitContext.AddDependency(new DeferredOperation(() => _disposed = true));
            }
        }

        public DescriptorImageInfo GetDescriptorInfo()
        {
            return new DescriptorImageInfo(CreateSampler(_context, TotalMipLevels), Image.GetMipView(_loadedMipLevels));
        }
    }
}