using JoltPhysicsSharp;

using RockEngine.Core.Rendering.Buffers;

using System.Numerics;

namespace RockEngine.Core.Physics
{
    internal class PhysicsDebugRenderer : DebugRenderer
    {
        private readonly GlobalGeometryBuffer _globalGeometryBuffer;

        public PhysicsDebugRenderer(GlobalGeometryBuffer globalGeometryBuffer)
        {
            _globalGeometryBuffer = globalGeometryBuffer;
        }

        protected override void DrawLine(Vector3 from, Vector3 to, JoltColor color)
        {
        }

        protected override void DrawText3D(Vector3 position, string? text, JoltColor color, float height = 0.5F)
        {
        }
    }
}
