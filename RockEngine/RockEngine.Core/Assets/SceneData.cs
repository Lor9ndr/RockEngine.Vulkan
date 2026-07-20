using MemoryPack;

using RockEngine.Core.ECS;

namespace RockEngine.Core.Assets
{
    [MemoryPackable]
    public partial class SceneData
    {
        public List<Entity> Entities { get; set; } = new List<Entity>();
    }
}