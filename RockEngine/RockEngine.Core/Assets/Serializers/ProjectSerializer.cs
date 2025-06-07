using Newtonsoft.Json;

using RockEngine.Core.Assets.AssetData;

namespace RockEngine.Core.Assets.Serializers
{
    public class ProjectSerializer : IAssetSerializer<ProjectData>
    {
        public ProjectData Deserialize(string json) => JsonConvert.DeserializeObject<ProjectData>(json);
        public string Serialize(ProjectData data) => JsonConvert.SerializeObject(data, Formatting.Indented);
    }
}
