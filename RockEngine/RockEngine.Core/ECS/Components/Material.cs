using RockEngine.Vulkan;

namespace RockEngine.Core.ECS.Components
{
    public struct Material 
    {
        public VkPipeline Pipeline;
        public Texture[] Textures;
    }
}
