using MessagePack;

using RockEngine.Core.Attributes;

using Silk.NET.Vulkan;

using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Core
{
    [MessagePackObject]
    public struct Vertex : IVertex
    {
        [Key(0)]
        public Vector4 Position;
        [Key(1)]
        public Vector4 Normal;
        [Key(2)]
        public Vector2 TexCoord;
        [Key(3)]
        public Vector4 Tangent;
        [Key(4)]
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

        public Vertex() 
        {
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
    [MessagePackObject]
    public struct PositionVertex : IVertex
    {
        [Key(0)]
        public Vector4 Position;

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
