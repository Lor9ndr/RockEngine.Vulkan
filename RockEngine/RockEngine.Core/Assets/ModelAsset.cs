using NLog;

using RockEngine.Core.DI;

using System.Text.Json.Serialization;

namespace RockEngine.Core.Assets
{
    public sealed class ModelAsset : Asset<ModelData>
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        public override string Type => "Model";

        [System.Text.Json.Serialization.JsonIgnore]
        public List<ModelPart> Parts { get; } = new List<ModelPart>();


        public void AddPart(ModelPart part)
        {
            Data ??= new ModelData();
            Data.Parts.Add(new ModelPartData
            {
                MeshAssetID = part.Mesh.ID,
                MaterialAssetID = part.Material.ID
            });
            Parts.Add(part);
        }

        public override void SetData(object data)
        {
            base.SetData(data);
            if(data is ModelData modelData)
            {
                Parts.Clear();

                var assetManager = IoC.Container.GetInstance<AssetRepository>();
                foreach (var partData in modelData.Parts)
                {
                    if(assetManager.TryGet(partData.MeshAssetID, out var meshAsset) && assetManager.TryGet(partData.MaterialAssetID, out var materialAsset))
                    {
                        Parts.Add(new ModelPart { Mesh = (MeshAsset)meshAsset, Material = (MaterialAsset)materialAsset });
                    }
                    else
                    {
                        _logger.Error("Failed to find mesh or material assets: mesh = {meshID}, material= {material}", partData.MeshAssetID, partData.MaterialAssetID);
                    }
                }
            }
               
        }
    }
    public class ModelData
    {
        public List<ModelPartData> Parts { get; set; } = new List<ModelPartData>();
    }

    public struct ModelPartData
    {
        public Guid MeshAssetID { get; set; }
        public Guid MaterialAssetID { get; set; }
    }

    public struct ModelPart
    {
        public MeshAsset Mesh { get; set; }
        public MaterialAsset Material { get; set; }
    }
}
