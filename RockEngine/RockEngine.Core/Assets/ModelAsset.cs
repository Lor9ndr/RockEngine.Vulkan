
using NLog;

using RockEngine.Assets;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;

using System.Text.Json.Serialization;

namespace RockEngine.Core.Assets
{
    public sealed class ModelAsset : Asset<ModelData>, IGpuResource
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private bool _loaded;

        public override string Type => "Model";

        [JsonIgnore]
        public List<ModelPart> Parts { get; private set; } = new();

        public bool GpuReady => _loaded;

        public void AddPart(ModelPart part)
        {
            Data ??= new ModelData();
            Data.Parts.Add(new ModelPartData
            {
                MeshAssetID = part.Mesh.AssetID,
                MaterialAssetID = part.Material.AssetID,
                Transform = part.Transform,
                Name = part.Name
            });
            Parts.Add(part);
            UpdateModified();
        }

        public void RemovePart(Guid meshId)
        {
            if (Data != null)
            {
                var index = Data.Parts.FindIndex(p => p.MeshAssetID == meshId);
                if (index >= 0)
                {
                    Data.Parts.RemoveAt(index);
                    Parts.RemoveAll(p => p.Mesh.AssetID == meshId);
                    UpdateModified();
                }
            }
        }

        public ModelPart? GetPart(Guid meshId)
        {
            return Parts.FirstOrDefault(p => p.Mesh.AssetID == meshId);
        }

        public override void SetData(object data)
        {
            base.SetData(data);

            if (data is ModelData modelData)
            {
                Parts.Clear();
                var assetManager = IoC.Container.GetInstance<IAssetRepository>();

                foreach (var partData in modelData.Parts)
                {
                    if (assetManager.TryGet(partData.MeshAssetID, out var meshAsset) &&
                        assetManager.TryGet(partData.MaterialAssetID, out var materialAsset))
                    {
                        Parts.Add(new ModelPart
                        {
                            Mesh = (MeshAsset)meshAsset,
                            Material = (MaterialAsset)materialAsset,
                            Transform = partData.Transform,
                            Name = partData.Name
                        });
                    }
                    else
                    {
                        _logger.Error("Failed to find mesh or material assets: mesh = {meshID}, material= {material}",
                            partData.MeshAssetID, partData.MaterialAssetID);
                    }
                }
            }
        }

        public override async Task LoadDataAsync()
        {
            await base.LoadDataAsync();

            // Load GPU resources for all parts
            foreach (var part in Parts)
            {
                try
                {
                    await part.Mesh.Asset.LoadGpuResourcesAsync();
                    await part.Material.Asset.LoadGpuResourcesAsync();
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to load GPU resources for model part {PartName}", part.Name);
                }
            }
            _loaded = true;

        }

        public async ValueTask LoadGpuResourcesAsync()
        {
            foreach (var part in Parts)
            {
                await part.Mesh.Asset.LoadGpuResourcesAsync();
                await part.Material.Asset.LoadGpuResourcesAsync();
            }
        }

        public void UnloadGpuResources()
        {
            foreach (var part in Parts)
            {
                part.Mesh.Asset.UnloadGpuResources();
                part.Material.Asset.UnloadGpuResources();
            }
        }

        public void Dispose()
        {
            UnloadGpuResources();
            UnloadData();
        }

        public override void UnloadData()
        {
            base.UnloadData();
            Parts.Clear();
        }
    }
}