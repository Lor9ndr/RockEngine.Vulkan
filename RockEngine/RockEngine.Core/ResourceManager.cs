using RockEngine.Vulkan;

namespace RockEngine.Core
{
    public class ResourceManager : IDisposable
    {
        private Dictionary<string, VkPipeline> _pipelines = new Dictionary<string, VkPipeline>();

        public VkPipeline GetPipeline(string pipelineName)
        {
            return _pipelines[pipelineName];
        }

        public IEnumerable<VkPipeline> GetPipelines()
        {
            return _pipelines.Values;
        }
        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
