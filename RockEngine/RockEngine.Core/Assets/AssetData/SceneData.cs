using Newtonsoft.Json;

namespace RockEngine.Core.Assets.AssetData
{
    [JsonObject(MemberSerialization.OptIn)]
    public class SceneData : IAssetData
    {
        [JsonProperty("entities")]
        public List<EntityData> Entities { get; set; } = new();

        [JsonProperty("dependencies")]
        public List<Guid> AssetDependencies { get; set; } = new();
    }
}
