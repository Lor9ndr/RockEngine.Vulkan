using RockEngine.Core.Attributes;

using Silk.NET.Vulkan;

using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Core
{
    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    public struct Vertex : IVertex
    {
        [Serialize]
        public Vector4 Position;
        [Serialize]
        public Vector4 Normal;
        [Serialize]
        public Vector2 TexCoord;
        private Vector2 _glslPadding1;
        [Serialize]
        public Vector4 Tangent;
        [Serialize]
        public Vector4 Bitangent;

        public static float Size = Marshal.SizeOf<Vertex>();

        public Vertex(Vector3 position, Vector3 normal, Vector2 texCoords)
        {
            Position = new Vector4(position, 0);
            Normal = new Vector4(normal, 0);
            TexCoord = texCoords;
        }
        public Vertex(float xp, float yp, float zp, float xc, float yc, float zc, float xt, float yt)
        {
            Position = new Vector4(xp, yp, zp,0);
            Normal = new Vector4(xc, yc, zc, 0);
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

    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    public struct PositionVertex : IVertex
    {
        public System.Numerics.Vector4 Position;

        public static float Size = Marshal.SizeOf<Vertex>();

        public PositionVertex(Vector3 position)
        {
            Position = new Vector4(position,0);
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
        };
    }

}
