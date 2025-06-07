
using Newtonsoft.Json;

namespace RockEngine.Core.Assets.AssetData
{
    [JsonObject(MemberSerialization.OptIn)]
    public class MaterialData : IAssetData
    {
        [JsonProperty("properties")]
        public Dictionary<string, MaterialProperty> Properties { get; set; } = new();
    }
    [JsonConverter(typeof(MaterialPropertyConverter))]
    public class MaterialProperty
    {
        public MaterialPropertyType Type { get; set; }
        public object Value { get; set; }
    }
}