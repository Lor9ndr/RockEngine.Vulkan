using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace RockEngine.Vulkan.Rendering
{
    internal class LightRenderer
    {
        [StructLayout(LayoutKind.Sequential, Pack = 16, Size = 64)]
        private struct LightData
        {
            public Vector3 position;
            public float intensity;
            public Vector3 color;
            public int type;
        }
    }
}
