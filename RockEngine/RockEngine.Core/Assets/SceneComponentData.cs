namespace RockEngine.Core.Assets
{
    public class SceneComponentData
    {
        public string TypeName { get; set; } = string.Empty;
        public required byte[] Data { get; set; }
    }
}