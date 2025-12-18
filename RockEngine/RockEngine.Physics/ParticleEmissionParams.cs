using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Managers
{
    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    public struct ParticleEmissionParams
    {
        public uint SystemId;
        public uint Count;
        private System.Numerics.Vector2 _glslPadding1;
        public System.Numerics.Vector4 Position;
        public System.Numerics.Vector4 BaseVelocity;
        public uint RandomSeed;
    }
}