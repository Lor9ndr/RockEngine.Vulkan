using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.VkObjects;

namespace RockEngine.Vulkan.Assets
{
    public record MeshData(string Name, Vertex[] Vertices, uint[] Indices, List<Texture> textures)
    {
    }

}
