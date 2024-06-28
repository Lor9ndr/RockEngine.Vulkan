using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using System.Numerics;

namespace RockEngine.Vulkan.GUI
{
    public class GuiWindow : GuiElement
    {
        public override uint VertexCount => 6; // Assuming a rectangle made of two triangles

        public override async Task Render(VulkanContext context, CommandBufferWrapper commandBuffer, BufferWrapper sharedBuffer)
        {
            // Bind the shared buffer and set up the pipeline for rendering
            // This is a simplified example and may need more setup depending on the actual Vulkan context and pipeline state
            commandBuffer.BindVertexBuffer(sharedBuffer, VertexOffset);
            commandBuffer.Draw(VertexCount,1,0,0);
        }

        public override async Task UpdateBuffer(VulkanContext context, BufferWrapper sharedBuffer)
        {
            // Define the vertices for the rectangle (two triangles)
            var vertices = new[]
            {
                new GuiVertex { Position = new Vector2(Position.X, Position.Y), Color = Color },
                new GuiVertex { Position = new Vector2(Position.X + Size.X, Position.Y), Color = Color },
                new GuiVertex { Position = new Vector2(Position.X, Position.Y + Size.Y), Color = Color },
                new GuiVertex { Position = new Vector2(Position.X + Size.X, Position.Y), Color = Color },
                new GuiVertex { Position = new Vector2(Position.X, Position.Y + Size.Y), Color = Color },
                new GuiVertex { Position = new Vector2(Position.X + Size.X, Position.Y + Size.Y), Color = Color }
            };

            // Update the shared buffer with the new vertices
            await sharedBuffer.SendDataAsync(vertices, VertexOffset);
        }
    }

}
