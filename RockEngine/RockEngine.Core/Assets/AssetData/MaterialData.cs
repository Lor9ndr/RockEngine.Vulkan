using Newtonsoft.Json;

namespace RockEngine.Core.Assets.AssetData
{
    public class MaterialData
    {
        public MaterialData()
        {
        }

        public string PipelineName { get; set; } = "Geometry"; 
        public string MaterialName { get; set; }

        public List<AssetReference<TextureAsset>> Textures { get; set; } = new List<AssetReference<TextureAsset>>();

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Dictionary<string, object>? Parameters { get; set; }
    }
}