using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using static RockEngine.Vulkan.ShaderReflectionData;

namespace RockEngine.Core.Rendering.Materials
{
    public interface ITypeBasedResourceProvider
    {
        Texture GetDefaultTexture(DescriptorSetLayoutBindingReflected binding, VulkanContext context);
        object GetDefaultPushConstant(PushConstantInfo pushConstant);
    }
}