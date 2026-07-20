using MemoryPack;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using SkiaSharp;

namespace RockEngine.Core.Assets
{
    [MemoryPackable]
    public sealed partial class TextureAsset : Asset<TextureData>, IGpuResource
    {
        public override string Type => "Texture";

        
        private Texture? _texture;
        
        private readonly SemaphoreSlim _gpuSemaphore = new(1, 1);
        
        private SKBitmap[]? _bitmaps; // CPU-side image data
        
        private bool _disposed;

        public TextureDimension TextureType => Data?.Dimension ?? TextureDimension.Texture2D;
        
        public bool GpuReady => _texture != null;

        [MemoryPackIgnore]
        public Texture? Texture => _texture;

        // Texture properties
        
        public uint Width => Data?.Width ?? 0;
        
        public uint Height => Data?.Height ?? 0;
        
        public TextureFormat Format => Data?.Format ?? TextureFormat.R8G8B8A8Unorm;
        
        public bool HasMipmaps => Data?.GenerateMipmaps ?? false;

        public TextureAsset()
        {
        }

        public override async Task LoadDataAsync()
        {
            await base.LoadDataAsync().ConfigureAwait(false);

            if (Data != null && Data.FilePaths.Count > 0)
            {
                await LoadImageDataAsync().ConfigureAwait(false);
            }
        }

        private async Task LoadImageDataAsync()
        {
            if (Data == null || Data.FilePaths.Count == 0)
            {
                return;
            }

            try
            {
                _bitmaps = await LoadBitmapsAsync(Data).ConfigureAwait(false);

                // Update dimensions from loaded bitmaps
                if (_bitmaps.Length > 0)
                {
                    Data.Width = (uint)_bitmaps[0].Width;
                    Data.Height = (uint)_bitmaps[0].Height;
                }
            }
            catch (Exception ex)
            {
                var logger = NLog.LogManager.GetCurrentClassLogger();
                logger.Error(ex, "Failed to load image data for texture {Name}", Name);
            }
        }

        private static async Task<SKBitmap[]> LoadBitmapsAsync(TextureData data)
        {
            var bitmaps = new SKBitmap[data.FilePaths.Count];
            
            var tasks = new Task[data.FilePaths.Count];

            for (var i = 0; i < data.FilePaths.Count; i++)
            {
                var filePath = data.FilePaths[i];
                
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"Texture file not found: {filePath}");
                }
                int currentIndex = i;
                tasks[i] = Task.Run(async () =>
                {
                    var bytes = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
                    var bitmap = SKBitmap.Decode(bytes) ?? throw new InvalidOperationException($"Failed to decode texture: {filePath}");
                    if (data.FlipVertically)
                    {
                        bitmap = FlipBitmapVertically(bitmap);
                    }

                    bitmaps[currentIndex] = bitmap;
                });
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
            return bitmaps;
        }

        private static SKBitmap FlipBitmapVertically(SKBitmap bitmap)
        {
            var flipped = new SKBitmap(bitmap.Width, bitmap.Height, bitmap.ColorType, bitmap.AlphaType);
            using var canvas = new SKCanvas(flipped);
            canvas.Scale(1, -1, bitmap.Width / 2f, bitmap.Height / 2f);
            canvas.DrawBitmap(bitmap, 0, 0);
            return flipped;
        }

        public async ValueTask LoadGpuResourcesAsync()
        {
            if (GpuReady)
            {
                return;
            }

            await _gpuSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (GpuReady)
                {
                    return;
                }

                if (!IsDataLoaded)
                {
                    await LoadDataAsync().ConfigureAwait(false);
                }

                if (Data == null)
                {
                    throw new InvalidOperationException("Texture data not loaded");
                }
                var context = IoC.Container.GetInstance<VulkanContext>();
                Data.GenerateMipmaps = true;
                // Use the new unified creation method
                _texture = await Texture2D.CreateAsync(context, Data, default).ConfigureAwait(false);

                // Clean up CPU-side bitmaps
                if (_bitmaps != null)
                {
                    foreach (var bitmap in _bitmaps)
                    {
                        bitmap?.Dispose();
                    }
                    _bitmaps = null;
                }
            }
            finally
            {
                _gpuSemaphore.Release();
            }
        }

        public void UnloadGpuResources()
        {
            _gpuSemaphore.Wait();
            try
            {
                _texture?.Dispose();
                _texture = null;

                // Clean up CPU data
                if (_bitmaps != null)
                {
                    foreach (var bitmap in _bitmaps)
                    {
                        bitmap?.Dispose();
                    }
                    _bitmaps = null;
                }
            }
            finally
            {
                _gpuSemaphore.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            UnloadGpuResources();
            _gpuSemaphore.Dispose();
            _disposed = true;
        }

        public override void UnloadData()
        {
            base.UnloadData();
            UnloadGpuResources();
        }


    }
}