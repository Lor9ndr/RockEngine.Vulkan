using RockEngine.Core.Rendering.Managers;
using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering.Materials
{
    public class MaterialTemplate
    {
        public string Name { get; }
        public ShaderReflectionData ReflectionData { get; }
        private readonly Dictionary<string, MaterialPassTemplate> _passTemplates = new();

        public IReadOnlyDictionary<string, MaterialPassTemplate> PassTemplates => _passTemplates;

        public MaterialTemplate(string name, ShaderReflectionData reflectionData)
        {
            Name = name;
            ReflectionData = reflectionData;
        }

        public void AddPassTemplate(string subpassName, MaterialPassTemplate passTemplate)
        {
            _passTemplates[subpassName] = passTemplate;
        }

        public Material CreateInstance(string instanceName, PipelineManager pipelineManager)
        {
            var material = new Material(instanceName);

            foreach (var (subpassName, passTemplate) in _passTemplates)
            {
                var pipeline = pipelineManager.GetPipelineByName(passTemplate.PipelineName);
                if (pipeline != null)
                {
                    var pass = passTemplate.CreateMaterialPass(pipeline);
                    material.AddPass(subpassName, pass);
                }
            }

            return material;
        }
    }
}