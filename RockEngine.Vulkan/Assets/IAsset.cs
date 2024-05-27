using System.Text.Json.Serialization;

namespace RockEngine.Vulkan.Assets
{
    public interface IAsset
    {
        public const string FILE_EXTENSION = ".asset";

        public Guid ID { get;}
        public string Name { get; set;}
        public string Path { get; set; }
        [JsonIgnore]
        public bool IsChanged { get; set; }
    }
}
