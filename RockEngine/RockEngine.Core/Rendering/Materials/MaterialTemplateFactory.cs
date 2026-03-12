using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Vulkan;

using System.Collections.Concurrent;

namespace RockEngine.Core.Rendering.Materials
{
    public class MaterialTemplateFactory : IMaterialTemplateFactory
    {
        private readonly PipelineManager _pipelineManager;
        private readonly IShaderReflectionProvider _reflectionProvider;
        private readonly ITypeBasedResourceProvider _resourceProvider;
        private readonly ConcurrentDictionary<string, MaterialTemplate> _templateCache = new();

        public MaterialTemplateFactory(
            PipelineManager pipelineManager,
            IShaderReflectionProvider reflectionProvider,
            ITypeBasedResourceProvider resourceProvider = null)
        {
            _pipelineManager = pipelineManager ?? throw new ArgumentNullException(nameof(pipelineManager));
            _reflectionProvider = reflectionProvider ?? throw new ArgumentNullException(nameof(reflectionProvider));
            _resourceProvider = resourceProvider ?? new TypeBasedResourceProvider();
        }

        public MaterialTemplate CreateTemplate(string pipelineName, RckPipeline pipeline)
        {
            ArgumentException.ThrowIfNullOrEmpty(pipelineName, nameof(pipelineName));
            ArgumentNullException.ThrowIfNull(pipeline, nameof(pipeline));

            var reflection = _reflectionProvider.GetPipelineReflection(pipeline.VkPipeline);
            var template = new MaterialTemplate(pipelineName, reflection);

            // Use the subpass metadata from the RckPipeline
            var passTemplate = new MaterialPassTemplate(pipeline.SubpassName, pipelineName, reflection, _resourceProvider);
            template.AddPassTemplate(pipeline.SubpassName, passTemplate);

            return template;
        }

        public MaterialTemplate GetOrCreateTemplate(string pipelineName)
        {
            ArgumentException.ThrowIfNullOrEmpty(pipelineName, nameof(pipelineName));
            return _templateCache.GetOrAdd(pipelineName, name =>
            {
                var pipeline = _pipelineManager.GetPipelineByName(name);
                return pipeline != null ? CreateTemplate(name, pipeline) : null;
            });
        }

        public MaterialTemplate GetOrCreateTemplateForSubpass(string subpassName)
        {
            if (string.IsNullOrEmpty(subpassName))
            {
                throw new ArgumentException("Subpass name cannot be null or empty", nameof(subpassName));
            }

            var pipeline = _pipelineManager.GetPipelineForSubpass(subpassName);
            return pipeline != null ? GetOrCreateTemplate(pipeline.Name) : null;
        }
    }
}