using RockEngine.Core.Rendering;

namespace RockEngine.Core.Assets
{
    public class SceneEntityData
    {
        public ulong ID { get; set; }
        public string Name { get; set; } = string.Empty;
        public ulong? ParentID { get; set; }
        public RenderLayer RenderLayerType { get; set; }
        public TransformData Transform { get; set; } = new();
        public List<SceneComponentData> Components { get; set; } = new List<SceneComponentData>();
    }
}