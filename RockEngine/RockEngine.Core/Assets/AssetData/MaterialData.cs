namespace RockEngine.Core.Assets.AssetData;

public class MaterialData
{
    public MaterialData()
    {
    }

    public string PipelineName { get; set; } = "Default";
    public List<AssetReference<TextureAsset>> TextureAssetIDs { get; set; } = new List<AssetReference<TextureAsset>>();
}