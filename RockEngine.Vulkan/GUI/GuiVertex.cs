using Silk.NET.Vulkan;

using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RockEngine.Vulkan.GUI
{
    [StructLayout(LayoutKind.Sequential)]
    public struct GuiVertex
    {
        public Vector2 Position;
        public Vector4 Color;

        public static int Size = Marshal.SizeOf<GuiVertex>();

        public GuiVertex(Vector2 position, Vector4 color)
        {
            Position = position;
            Color = color;
        }

        public static VertexInputBindingDescription GetBindingDescription() => new VertexInputBindingDescription
        {
            Binding = 0,
            Stride = (uint)Size,
            InputRate = VertexInputRate.Vertex
        };

        public static VertexInputAttributeDescription[] GetAttributeDescriptions() =>
            [
            new VertexInputAttributeDescription
            {
                Binding = 0,
                Location = 0,
                Format = Format.R32G32Sfloat,
                Offset = (uint)Marshal.OffsetOf<GuiVertex>(nameof(Position))
            },
            new VertexInputAttributeDescription
            {
                Binding = 0,
                Location = 1,
                Format = Format.R32G32B32A32Sfloat,
                Offset = (uint)Marshal.OffsetOf<GuiVertex>(nameof(Color))
            }
        ];
    }
}
