using RockEngine.Vulkan.VkObjects;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.Rendering.MaterialRendering
{
    public class Material
    {
        public EffectTemplate Original;
        public PerPassData<DescriptorSet> PassSets;
        public List<Texture> Textures;
        public Dictionary<string, object> Parameters;

        public Material(EffectTemplate original, List<Texture> textures, Dictionary<string, object> parameters)
        {
            Original = original;
            Textures = textures;
            Parameters = parameters;
            PassSets = new PerPassData<DescriptorSet>();
        }
    }
}
