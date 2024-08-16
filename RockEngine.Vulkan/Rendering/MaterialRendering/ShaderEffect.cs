using RockEngine.Vulkan.VkObjects;

namespace RockEngine.Vulkan.Rendering.MaterialRendering
{
    public class ShaderEffect
    {
        /// <summary>
        /// ShaderStages, filled with shader modules and shader stage
        /// </summary>
        public List<ShaderModuleWrapper> Shaders;

        public ShaderEffect(List<ShaderModuleWrapper> stages)
        {
            Shaders = stages;
        }
    }
}