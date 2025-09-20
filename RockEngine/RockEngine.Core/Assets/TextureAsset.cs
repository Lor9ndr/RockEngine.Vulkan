using RockEngine.Core.Assets.RockEngine.Core.Assets;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;


namespace RockEngine.Core.Assets
{
    public sealed class TextureAsset : Asset<TextureData>, IGpuResource
    {
        public override string Type => "Texture";
        private Texture? _texture;
        private readonly SemaphoreSlim _gpuSemaphore = new(1,1);
        private bool _disposed;

        public TextureType TextureType => Data?.Type ?? TextureType.Unknown;
        public bool GpuReady => _texture != null;
        public Texture? Texture => _texture;

        public TextureAsset()
        {
        }


        public async ValueTask LoadGpuResourcesAsync()
        {
            if (GpuReady)
            {
                return;
            }
            await _gpuSemaphore.WaitAsync();
            try
            {
                if (GpuReady)
                {
                    return;
                }
                if (!IsDataLoaded)
                {
                    await LoadDataAsync();
                }

                var context = IoC.Container.GetInstance<VulkanContext>();
                
                _texture = Data!.Type switch
                {
                    TextureType.Texture2D => await Texture2D.CreateAsync(context, Data.FilePaths[0]),
                    TextureType.TextureCube => await Texture3D.CreateCubeMapAsync(context, Data.FilePaths.ToArray()),
                    _ => throw new NotSupportedException($"Texture type {Data.Type} not supported"),
                };
                

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
                _texture = null;
                UnloadData();
            }

            finally
            {
                _gpuSemaphore.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            UnloadGpuResources();
            _disposed = true;
        }
    }
}