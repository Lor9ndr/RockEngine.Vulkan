using RockEngine.Core.Rendering.Objects;
using RockEngine.Vulkan;

namespace RockEngine.Core.Registries
{
    public class PipelineRegistry : IRegistry<RckPipeline, string>
    {
        private readonly Dictionary<string, RckPipeline> _pipelines = new();

      
        public RckPipeline? Get(string key)
        {
            if(_pipelines.TryGetValue(key, out var value))
            {
                return value;
            }
            return null;
        }

        public void Register(string key, RckPipeline value)
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

        public IEnumerable<RckPipeline> GetAll()
        {
            return _pipelines.Values;
        }
    }
}
