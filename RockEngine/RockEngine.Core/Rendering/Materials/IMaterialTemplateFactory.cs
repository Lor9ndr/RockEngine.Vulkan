using RockEngine.Core.Rendering.Objects;
using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering.Materials
{
    public interface IMaterialTemplateFactory
    {
        MaterialTemplate CreateTemplate(string pipelineName, RckPipeline pipeline);
        MaterialTemplate GetOrCreateTemplate(string pipelineName);
        MaterialTemplate GetOrCreateTemplateForSubpass(string subpassName);
    }
}