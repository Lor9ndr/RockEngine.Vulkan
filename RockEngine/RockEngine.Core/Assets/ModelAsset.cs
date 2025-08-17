using RockEngine.Core.DI;

namespace RockEngine.Core.Assets
{
    public sealed class ModelAsset : Asset<ModelData>
    {
        public override string Type => "Model";
        public List<ModelPart> Parts { get; } = new List<ModelPart>();


        public void AddPart(ModelPart part)
        {
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

                var assetManager = IoC.Container.GetInstance<IAssetStorage>();
                foreach (var partData in modelData.Parts)
                {
                    var mesh = assetManager.GetAsset<MeshAsset>(partData.MeshAssetID);
                    var material = assetManager.GetAsset<MaterialAsset>(partData.MaterialAssetID);
                    Parts.Add(new ModelPart { Mesh = mesh, Material = material });
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
