namespace RockEngine.Core.Assets
{
    public class MaterialData
    {
        public string PipelineName { get; set; } = "Default";
        public List<Guid> TextureAssetIDs { get; set; } = new List<Guid>();
    }

}