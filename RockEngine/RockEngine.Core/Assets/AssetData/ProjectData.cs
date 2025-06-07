using Newtonsoft.Json;

namespace RockEngine.Core.Assets.AssetData
{
    [JsonObject(MemberSerialization.OptIn)]
    public class ProjectData : IAssetData
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("scenes")]
        public List<Guid> SceneIDs { get; set; } = new();

        [JsonProperty("assets")]
        public List<Guid> AssetIDs { get; set; } = new();

        [JsonProperty("settings")]
        public ProjectSettings Settings { get; set; } = new();
    }
}
