using NLog;

using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.DI;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

namespace RockEngine.Core.Assets
{
    public sealed class MaterialAsset : Asset<MaterialData>, IGpuResource, IDisposable
    {
        public override string Type => "Material";

        public bool GpuReady => MaterialInstance is not null;
        public Material? MaterialInstance { get; private set; }
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();


        public async ValueTask LoadGpuResourcesAsync()
        {
            if (GpuReady) return;
            if (!IsDataLoaded)  await LoadDataAsync();

            var assetManager = IoC.Container.GetInstance<AssetManager>();
            var pipelineManager = IoC.Container.GetInstance<PipelineManager>();
            var pipeline = pipelineManager.GetPipelineByName(Data!.PipelineName);

            var textures = new List<Texture>();

            foreach (var texture in Data.TextureAssetIDs)
            {
                var textureAsset = await assetManager.LoadAssetByIdAsync<TextureAsset>(texture.AssetID);
                if(textureAsset != null)
                {
                    await textureAsset.LoadGpuResourcesAsync();
                    textures.Add(textureAsset.Texture!);
                }
                else
                {
                    _logger.Warn("Failed to find texture {0}", texture);
                    textures.Add(Texture2D.GetEmptyTexture(VulkanContext.GetCurrent()));
                }
            }
            MaterialInstance = new Material(pipeline, textures);
        }

        public void UnloadGpuResources()
        {
            MaterialInstance = null;
            UnloadData();
        }

        public void Dispose()
        {
            UnloadGpuResources();
        }
    }
}