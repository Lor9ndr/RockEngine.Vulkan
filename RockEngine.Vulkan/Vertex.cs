using Silk.NET.Vulkan;

using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Vulkan
{
    public struct Vertex
    {
        public Vector2 Position;
        public Vector3 Color;

        public static float Size = Marshal.SizeOf<Vertex>();

        public Vertex(Vector2 position, Vector3 color)
        {
            Position = position;
            Color = color;
        }

        public static VertexInputBindingDescription GetBindingDescription() => new VertexInputBindingDescription()
        {
            Binding = 0,
            Stride = (uint)Size,
            InputRate = VertexInputRate.Vertex
        };

        public static VertexInputAttributeDescription[] GetAttributeDescriptions() => new VertexInputAttributeDescription[2]
        {
            new VertexInputAttributeDescription()
            {
                 Binding = 0,
                 Location = 0,
                 Format = Format.R32G32Sfloat,
                 Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(Position))
            },
            new VertexInputAttributeDescription()
            {
                 Binding = 0,
                 Location = 1,
                 Format = Format.R32G32Sfloat,
                 Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(Color))
            }
        };
    }
}
