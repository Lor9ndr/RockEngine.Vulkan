using System.Numerics;

namespace RockEngine.Vulkan.ECS
{
    public class Transform : Component
    {
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; } = Quaternion.Identity;
        public Vector3 Scale { get; set; } = Vector3.One;

        public override Task Update()
        {
            // update here passing model into the pipeline 
            throw new NotImplementedException();
        }
    }
}
