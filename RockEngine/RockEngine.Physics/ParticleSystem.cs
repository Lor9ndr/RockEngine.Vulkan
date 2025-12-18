using RockEngine.Core.Attributes;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;

using System.Numerics;

namespace RockEngine.Physics
{
    public partial class ParticleSystem : Component
    {
        public uint MaxParticles { get; set; } = 1000;
        public float EmissionRate { get; set; } = 10.0f;
        public Vector3 Gravity { get; set; } = new Vector3(0, -9.81f, 0);
        public float ParticleLifetime { get; set; } = 5.0f;
        public Vector3 StartVelocity { get; set; } = new Vector3(0, 5.0f, 0);
        public Vector3 VelocityRandomness { get; set; } = new Vector3(1.0f);
        public float StartSize { get; set; } = 0.1f;
        public float SizeRandomness { get; set; } = 0.05f;

        [SerializeIgnore]
        public uint ParticleBufferId { get; internal set; }

        private float _emissionAccumulator = 0.0f;

        public override ValueTask OnStart(WorldRenderer renderer)
        {
            var physicsManager = renderer.PhysicsManager;
            physicsManager?.RegisterParticleSystem(this);
            return default;
        }

        public override async ValueTask Update(WorldRenderer renderer)
        {
            var physicsManager = renderer.PhysicsManager;
            if (physicsManager != null && physicsManager.IsInitialized)
            {
                // Update particle emission
                _emissionAccumulator += EmissionRate * Time.DeltaTime;
                uint particlesToEmit = (uint)_emissionAccumulator;

                if (particlesToEmit > 0)
                {
                    _emissionAccumulator -= particlesToEmit;
                    await physicsManager.EmitParticlesAsync(this, particlesToEmit);
                }
            }
        }

        public override void Destroy()
        {
            var physicsManager = IoC.Container.GetInstance<PhysicsManager>();
            physicsManager?.UnregisterParticleSystem(this);
            base.Destroy();
        }
    }
}