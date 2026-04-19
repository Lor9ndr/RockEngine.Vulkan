using RockEngine.Vulkan;

using static RockEngine.Vulkan.ShaderReflectionData;

namespace RockEngine.Core.Rendering.Materials
{
    public interface ITypeBasedResourceProvider
    {
        object GetDefaultPushConstant(PushConstantInfo pushConstant);
        object? GetDefaultResource(BindingInfo info);
    }
}