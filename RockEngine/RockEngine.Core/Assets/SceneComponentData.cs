using System.Text.Json.Nodes;

namespace RockEngine.Core.Assets
{
    public class SceneComponentData
    {
        public string TypeName { get; set; } = string.Empty;
        public byte[] Data { get; set; }
    }
}