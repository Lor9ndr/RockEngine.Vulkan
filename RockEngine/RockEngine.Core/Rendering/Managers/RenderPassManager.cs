using RockEngine.Core.Registries;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public class RenderPassManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly IRegistry<EngineRenderPass, Type> _registry;

        public RenderPassManager(VulkanContext context, IRegistry<EngineRenderPass, Type> registry)
        {
            _context = context;
            _registry = registry;
        }

        public EngineRenderPass? GetRenderPass<T>() where T:IRenderPassStrategy
        { 
            return _registry.Get(typeof(T));
        }


        public EngineRenderPass CreateRenderPass<T>(VkRenderPass renderPass) where T : IRenderPassStrategy
        {
            var engineRenderPass =  new EngineRenderPass(renderPass);
            _registry.Register(typeof(T),engineRenderPass);
            return engineRenderPass;
        }
        public EngineRenderPass CreateRenderPass(VkRenderPass renderPass, Type type) 
        {
            var engineRenderPass = new EngineRenderPass(renderPass);
            _registry.Register(type, engineRenderPass);
            return engineRenderPass;
        }

        public void Dispose()
        {
            _registry.Dispose();
        }
    }
}
