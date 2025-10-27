using NLog;

using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Core.ResourceProviders;

namespace RockEngine.Core.Assets
{
    public sealed class MaterialAsset : Asset<MaterialData>, IGpuResource,  IResourceProvider<Material>, IDisposable
    {
        public override string Type => "Material";
        public bool GpuReady => MaterialInstance is not null;
        public Material? MaterialInstance { get; private set; }

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public async ValueTask LoadGpuResourcesAsync()
        {
            if (GpuReady)
            {
                return;
            }

            if (!IsDataLoaded)
            {
                await LoadDataAsync();
            }

            try
            {
                await CreateMaterialAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to create material '{MaterialName}'", Name);
                throw;
            }
        }

        private async Task CreateMaterialAsync()
        {
            var templateManager = IoC.Container.GetInstance<MaterialTemplateManager>();
            var assetManager = IoC.Container.GetInstance<AssetManager>();

            // Create material from template
            MaterialInstance = templateManager.CreateMaterialFromTemplate(Data!.PipelineName, Name ?? "Material");

            // Load and bind textures
            await LoadAndBindTextures(assetManager);

            _logger.Debug("Created material '{MaterialName}' with template '{Template}'", Name, Data.PipelineName);
        }

        private async Task LoadAndBindTextures(AssetManager assetManager)
        {
            if (MaterialInstance == null || Data?.Textures == null)
            {
                return;
            }

            var loadedTextures = new List<Texture>(Data.Textures.Count);
            for (int i = 0; i < Data.Textures.Count; i++)
            {
                var textureRef = Data.Textures[i];
                
                var textureAsset = await textureRef.GetAssetAsync();

                if (textureAsset != null)
                {
                    await textureAsset.LoadGpuResourcesAsync();
                    MaterialInstance.BindResource(new TextureBinding(2, (uint)i, 0, 1, textureAsset.Texture));
                }
                else
                {
                    _logger.Warn("Failed to find texture {ID}",textureRef.AssetID);
                }
            }
        }

        public void UnloadGpuResources()
        {
            MaterialInstance?.Dispose();
            MaterialInstance = null;
        }

        public void Dispose() => UnloadGpuResources();

        public async ValueTask<Material> GetAsync()
        {
            if (MaterialInstance != null)
            {
                return MaterialInstance;
            }

            await LoadGpuResourcesAsync();
            return MaterialInstance!;
        }
    }
}