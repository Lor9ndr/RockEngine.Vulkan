using Silk.NET.Vulkan;

using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Core
{
    public struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord;


        /*public Vector3 Tangent;
        public Vector3 Bitangent;*/

        public static float Size = Marshal.SizeOf<Vertex>();

        public Vertex(Vector3 position, Vector3 normal, Vector2 texCoords)
        {
            Position = position;
            Normal = normal;
            TexCoord = texCoords;
        }
        public Vertex(float xp, float yp, float zp, float xc, float yc, float zc, float xt, float yt)
        {
            Position = new Vector3(xp, yp, zp);
            Normal = new Vector3(xc, yc, zc);
            TexCoord = new Vector2(xt, yt);
        }

        public static VertexInputBindingDescription GetBindingDescription() => new VertexInputBindingDescription()
        {
            Binding = 0,
            Stride = (uint)Size,
            InputRate = VertexInputRate.Vertex
        };

        public static VertexInputAttributeDescription[] GetAttributeDescriptions() => new VertexInputAttributeDescription[]
        {
            new VertexInputAttributeDescription()
            {
                 Binding = 0,
                 Location = 0,
                 Format = Format.R32G32B32Sfloat,
                 Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(Position))
            },
            new VertexInputAttributeDescription()
            {
                 Binding = 0,
                 Location = 1,
                 Format = Format.R32G32B32Sfloat,
                 Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(Normal))
            },
             new VertexInputAttributeDescription()
            {
                 Binding = 0,
                 Location = 2,
                 Format = Format.R32G32Sfloat,
                 Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(TexCoord))
            },
        };
    }
}
