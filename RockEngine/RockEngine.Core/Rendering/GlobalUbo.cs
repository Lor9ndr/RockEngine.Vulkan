using System.Numerics;

namespace RockEngine.Core.Rendering
{
    namespace RockEngine.Core.Rendering
    {
        public sealed class GlobalUbo : UniformBuffer
        {
            public Matrix4x4 ViewProjection { get; set; } // Add other global data if needed

            public GlobalUbo(string name, uint bindingLocation, ulong size)
                : base(name, bindingLocation, size, false) { }
        }
    }

}
