using System.Numerics;
using Silk.NET.Vulkan;

namespace RockEngine.Core.ECS.Components.UI
{
    public struct UIVertex : IVertex
    {
        public Vector2 Position;    // Screen-space position (NDC or pixel?)
        public Vector2 TexCoord;
        public Vector4 Color;       // RGBA
        public uint TextureId;      // Bindless index into global texture array

        public static uint SizeInBytes => (uint)sizeof(UIVertex);

        public static VertexInputAttributeDescription[] GetAttributeDescriptions()
        {
           return new[]
            {
                new VertexInputAttributeDescription { Binding = 0, Location = 0, Format = Format.R32G32Sfloat, Offset = 0 },             // Position
                new VertexInputAttributeDescription { Binding = 0, Location = 1, Format = Format.R32G32Sfloat, Offset = 8 },             // TexCoord
                new VertexInputAttributeDescription { Binding = 0, Location = 2, Format = Format.R32G32B32A32Sfloat, Offset = 16 },       // Color
                new VertexInputAttributeDescription { Binding = 0, Location = 3, Format = Format.R32Uint, Offset = 32 }                 // TextureId
            };
        }

        public static VertexInputBindingDescription GetBindingDescription()
        {
            return new VertexInputBindingDescription
            {
                Binding = 0,
                Stride = SizeInBytes,
                InputRate = VertexInputRate.Vertex
            };
        }
    }
}