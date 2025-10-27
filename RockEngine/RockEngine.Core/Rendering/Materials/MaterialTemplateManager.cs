using RockEngine.Core.DI;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Vulkan;

using System.Collections.Concurrent;

namespace RockEngine.Core.Rendering.Materials
{
    public class MaterialTemplateManager : IDisposable
    {
        private readonly IMaterialTemplateFactory _factory;
        private readonly ConcurrentDictionary<string, MaterialTemplate> _templates = new();
        private bool _disposed;

        public IReadOnlyDictionary<string, MaterialTemplate> Templates => _templates;

        public MaterialTemplateManager(IMaterialTemplateFactory factory)
        {
            _factory = factory;
            InitializeDefaultTemplates();
        }

        private void InitializeDefaultTemplates()
        {
            var defaultPipelines = new[] { "Geometry", "Solid", "Skybox", "DeferredLighting" };

            foreach (var pipelineName in defaultPipelines)
            {
                try
                {
                    GetOrCreateTemplate(pipelineName);
                }
                catch
                {
                    // Pipeline might not be created yet
                }
            }
        }

        public MaterialTemplate GetOrCreateTemplate(string pipelineName)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return _templates.GetOrAdd(pipelineName, _factory.GetOrCreateTemplate);
        }

        public Material CreateMaterialFromTemplate(string pipelineName, string materialName)
        {
            var template = GetOrCreateTemplate(pipelineName);
            var pipelineManager = IoC.Container.GetInstance<PipelineManager>();
            return template.CreateInstance(materialName, pipelineManager);
        }

        public void RegisterTemplate(MaterialTemplate template)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _templates[template.Name] = template;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _templates.Clear();
            _disposed = true;
        }
    }
}