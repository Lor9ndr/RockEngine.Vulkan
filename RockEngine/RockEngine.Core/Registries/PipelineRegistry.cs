using RockEngine.Vulkan;

namespace RockEngine.Core.Registries
{
    public class PipelineRegistry : IRegistry<VkPipeline, string>
    {
        private readonly Dictionary<string, VkPipeline> _pipelines = new();

      
        public VkPipeline? Get(string key)
        {
            if(_pipelines.TryGetValue(key, out var value))
            {
                return value;
            }
            return null;
        }

        public void Register(string key, VkPipeline value)
        {
            _pipelines[key] = value;
        }

        public void Unregister(string key)
        {
            _pipelines.Remove(key);
        }
        public void Dispose()
        {
            foreach (var item in _pipelines)
            {
                item.Value.Dispose();
            }
        }
    }
}
