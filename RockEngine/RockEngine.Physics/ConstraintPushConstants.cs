using System.Runtime.InteropServices;

namespace RockEngine.Core.ECS.Components
{


namespace RockEngine.Core.ECS.Components
    {
        [StructLayout(LayoutKind.Sequential, Pack = 16)]
        public struct ConstraintPushConstants
        {
            public uint ConstraintIndex;
            public uint SolverIteration;
            public float TimeStep;
            public float ErrorReductionParameter;
            public float Damping;
            public float ImpulseClamp;
            private System.Numerics.Vector2 _glslPadding1;
        }
    }
}