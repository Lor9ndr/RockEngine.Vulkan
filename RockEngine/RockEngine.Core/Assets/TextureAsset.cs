
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
        private readonly Lock _gpuLock = new();
        private bool _disposed;

     

        public TextureType TextureType => Data?.Type ?? TextureType.Unknown;
        public bool GpuReady => _texture != null;
        public Texture? Texture => _texture;

        public TextureAsset()
        {
        }


        public async ValueTask LoadGpuResourcesAsync()
        {
            if (GpuReady) return;
            if (!IsDataLoaded)   await LoadDataAsync();


            var context = IoC.Container.GetInstance<VulkanContext>();

            _texture = Data!.Type switch
            {
                TextureType.Texture2D => await Texture2D.CreateAsync(context, Data.FilePaths[0]),
                TextureType.TextureCube => await Texture3D.CreateCubeMapAsync(context, Data.FilePaths.ToArray()),
                _ => throw new NotSupportedException($"Texture type {Data.Type} not supported"),
            };

        }

        public void UnloadGpuResources()
        {
            lock (_gpuLock)
            {
                _texture?.Dispose();
                _texture = null;
                UnloadData();
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