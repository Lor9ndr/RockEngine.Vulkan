using Newtonsoft.Json;

namespace RockEngine.Core.Assets.AssetData
{
    [JsonObject(MemberSerialization.OptIn)]
    public class MeshData : IAssetData
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("vertices")]
        public Vertex[] Vertices { get; set; }

        [JsonProperty("indices")]
        public uint[] Indices { get; set; }

        [JsonProperty("texture_paths")]
        public List<string> TexturePaths { get; set; } = new List<string>();

        [JsonProperty("source_path")]
        public string SourcePath { get; set; }

        public MeshData() { }

        public MeshData(string name, Vertex[] vertices, uint[] indices, List<string> texturePaths)
        {
            Name = name;
            Vertices = vertices;
            Indices = indices;
            TexturePaths = texturePaths;
        }
        public MeshData(string name, Vertex[] vertices, uint[] indices )
        {
            Name = name;
            Vertices = vertices;
            Indices = indices;
            TexturePaths =  new List<string>();
        }
    }
}
