using Newtonsoft.Json;

using RockEngine.Core.Assets.AssetData;

namespace RockEngine.Core.Assets.Serializers
{
    public class MeshSerializer : IAssetSerializer<MeshData>
    {
        public MeshData Deserialize(string json) => JsonConvert.DeserializeObject<MeshData>(json);
        public string Serialize(MeshData data) => JsonConvert.SerializeObject(data, Formatting.Indented);
    }
}
