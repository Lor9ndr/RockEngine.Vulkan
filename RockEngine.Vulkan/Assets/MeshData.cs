using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.VkObjects;

namespace RockEngine.Vulkan.Assets
{
    public record struct MeshData(string Name, Vertex[] Vertices, uint[]? Indices, List<Texture>? textures)
    {
    }

}
