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
        public Vector3 Tangent;
        public Vector3 Bitangent;

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
            new VertexInputAttributeDescription(0, 0, Format.R32G32B32Sfloat, 0),
            new VertexInputAttributeDescription(1, 0, Format.R32G32B32Sfloat, (uint)Marshal.OffsetOf<Vertex>(nameof(Normal))),
            new VertexInputAttributeDescription(2, 0, Format.R32G32Sfloat, (uint)Marshal.OffsetOf<Vertex>(nameof(TexCoord))),
            new VertexInputAttributeDescription(3, 0, Format.R32G32B32Sfloat, (uint)Marshal.OffsetOf<Vertex>(nameof(Tangent))),
            new VertexInputAttributeDescription(4, 0, Format.R32G32B32Sfloat, (uint)Marshal.OffsetOf<Vertex>(nameof(Bitangent)))
        };
    }
}
