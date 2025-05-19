using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.ResourceBindings;

using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering
{
    namespace RockEngine.Core.Rendering
    {
        public sealed class GlobalUbo : UniformBuffer
        {
            private readonly UniformBufferBinding _binding;

            public GlobalUboData GlobalData { get; set; }

            public GlobalUbo(string name, uint bindingLocation)
                : base(name, bindingLocation, (ulong)Marshal.SizeOf<GlobalUboData>(), Marshal.SizeOf<GlobalUboData>(), false) 
                { 
                    _binding = new UniformBufferBinding(this, 0,0);
                }

            public async Task UpdateAsync()
            {
                await this.UpdateAsync(GlobalData);
            }

            internal ResourceBinding GetBinding()
            {
                return _binding;
            }

            public struct GlobalUboData
            {
                public Matrix4x4 ViewProjection;
                public Vector3 Position;
               /* public Vector4 ClusterSizes;
                public Vector4 ScreenToView;*/
            }
        }

    }
}
