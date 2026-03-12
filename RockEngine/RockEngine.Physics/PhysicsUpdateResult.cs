using static RockEngine.Core.Rendering.Managers.PhysicsManager;

namespace RockEngine.Core.Rendering.Managers
{
    public class PhysicsUpdateResult
    {
        public GPURigidbody[] RigidbodyData { get; set; }
        public GPUCollider[] ColliderData { get; set; }
        public GPUParticle[] ParticleData { get; set; }
        public DateTime Timestamp { get; set; }
        public float FrameTime { get; set; }

        // Collision events from this frame
        public List<CollisionEvent> CollisionEvents { get; set; } = new();
        public List<TriggerEvent> TriggerEvents { get; set; } = new();
    }
}