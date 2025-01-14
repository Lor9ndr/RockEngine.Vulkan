using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering
{
    namespace RockEngine.Core.Rendering
    {
        public sealed class GlobalUbo : UniformBuffer
        {
            public Matrix4x4 ViewProjection { get; set; }

            public GlobalUbo(string name, uint bindingLocation, ulong size)
                : base(name, bindingLocation, size, Marshal.SizeOf<Matrix4x4>(), false) { }

            public ValueTask UpdateAsync()
            {
                return this.UpdateAsync(ViewProjection);
            }
        }
    }
}
