using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Managers
{
    public partial class PhysicsManager
    {
        [StructLayout(LayoutKind.Sequential, Pack = 16)]
        public struct GPUParticle
        {
            public System.Numerics.Vector4 Position;
            public System.Numerics.Vector4 Velocity;
            public System.Numerics.Vector4 Acceleration;
            public float Lifetime;
            public float MaxLifetime;
            public float Size;
            public uint Alive;
        }
    }
}