using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

namespace RockEngine.Core.ECS.Components
{
    public struct Material 
    {
        public VkPipeline Pipeline;
        public Texture[] Textures;
       
        public List<ResourceBinding> Bindings;
        public Material(VkPipeline pipeline, Texture[] textures):this()
        {
            Pipeline = pipeline;
            Textures = textures;
        }
        public Material()
        {
            Bindings = new List<ResourceBinding>();
        }

        public readonly void AddBinding(ResourceBinding binding)
        {
            Bindings.Add(binding);
        }
     
        internal void RemoveBinding(ResourceBinding binding)
        {
            Bindings.Remove(binding);
        }
    }
}
