using Newtonsoft.Json;

using RockEngine.Core.Assets.AssetData;

namespace RockEngine.Core.Assets.Serializers
{
    public class SceneSerializer : IAssetSerializer<SceneData>
    {
        public SceneData Deserialize(string json) => JsonConvert.DeserializeObject<SceneData>(json);
        public string Serialize(SceneData data) => JsonConvert.SerializeObject(data, Formatting.Indented);
    }


}
