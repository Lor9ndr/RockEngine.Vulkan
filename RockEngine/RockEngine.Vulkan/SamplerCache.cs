using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public class SamplerCache : IDisposable
    {
        private readonly Dictionary<SamplerKey, VkSampler> _samplers = new();
        private readonly VulkanContext _context;

        public SamplerCache(VulkanContext context)
        {
            _context = context;
        }

        public VkSampler GetSampler(in SamplerCreateInfo ci)
        {
            var key = new SamplerKey(ci);
            if (!_samplers.TryGetValue(key, out var sampler))
            {
                sampler = VkSampler.Create(_context, ci);
                _samplers[key] = sampler;
            }
            if (sampler.IsDisposed)
            {
                sampler = VkSampler.Create(_context, ci);
                _samplers[key] = sampler;
            }
            return sampler;
        }
        

        private readonly record struct SamplerKey(SamplerCreateInfo CreateInfo);


        public void Dispose()
        {
            foreach (var item in _samplers)
            {
                item.Value.Dispose();
            }
        }
    }
}
