using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Managers
{
    public partial class PhysicsManager
    {
        [StructLayout(LayoutKind.Sequential, Pack = 16)]
        private struct ParticleEmissionRequest
        {
            public uint ParticleSystemId;
            public uint Count;
            private System.Numerics.Vector2 _glslPadding1;
            public System.Numerics.Vector4 Position;
            public System.Numerics.Vector4 BaseVelocity;
        }
    }
}