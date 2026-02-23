using MessagePack;

using RockEngine.Core.ECS;

namespace RockEngine.Core.Assets
{
    [MessagePackObject]
    public class SceneData
    {
        [Key(0)]
        public List<Entity> Entities { get; set; } = new List<Entity>();
    }
}