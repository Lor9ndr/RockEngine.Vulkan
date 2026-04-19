using System.Collections.Concurrent;
using RockEngine.Core.Rendering.Managers;

namespace RockEngine.Core.Rendering.Materials
{
    public class MaterialTemplateManager : IDisposable
    {
        private readonly IMaterialTemplateFactory _factory;
        private readonly PipelineManager _pipelineManager;
        private readonly ConcurrentDictionary<string, MaterialTemplate> _templates = new();
        private bool _disposed;

        public IReadOnlyDictionary<string, MaterialTemplate> Templates => _templates;

        public MaterialTemplateManager(IMaterialTemplateFactory factory, PipelineManager pipelineManager)
        {
            _factory = factory;
            _pipelineManager = pipelineManager;
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
            return template.CreateInstance(materialName, _pipelineManager);
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