using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public class RenderPassManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly List<EngineRenderPass> _renderPasses;

        public RenderPassManager(VulkanContext context)
        {
            _context = context;
            _renderPasses = new List<EngineRenderPass>();
        }

        public EngineRenderPass? GetRenderPass(string name)
        {
            foreach (var item in _renderPasses)
            {
                if (name.Equals(item.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }
            return null;
        }


        internal EngineRenderPass CreateRenderPass(VkRenderPass renderPass, string name)
        {
            
            var engineRenderPass =  new EngineRenderPass(name, renderPass);
            _renderPasses.Add(engineRenderPass);
            return engineRenderPass;
        }

        public void Dispose()
        {
            foreach (var renderPass in _renderPasses)
            {
                renderPass.Dispose();
            }
            _renderPasses.Clear();
        }


    }

}
