using RockEngine.Core.Registries;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Net.Mail;

namespace RockEngine.Core.Rendering.Managers
{
    public class RenderPassManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly IRegistry<RckRenderPass, Type> _registry;

        public RenderPassManager(VulkanContext context, IRegistry<RckRenderPass, Type> registry)
        {
            _context = context;
            _registry = registry;
        }

        public RckRenderPass? GetRenderPass<T>() where T:IRenderPassStrategy
        { 
            return _registry.Get(typeof(T));
        }


        public RckRenderPass CreateRenderPass<T>(VkRenderPass renderPass, params IRenderSubPass[] subPasses) where T : IRenderPassStrategy
        {
            var engineRenderPass =  new RckRenderPass(renderPass, subPasses);
            _registry.Register(typeof(T),engineRenderPass);
            return engineRenderPass;
        }
        public RckRenderPass CreateRenderPass(VkRenderPass renderPass, Type passProvider, params IRenderSubPass[] subPasses) 
        {
            var engineRenderPass = new RckRenderPass(renderPass,  subPasses);
            _registry.Register(passProvider, engineRenderPass);
            return engineRenderPass;
        }
        public void Register(RckRenderPass renderPass, Type passProvider)
        {
            _registry.Register(passProvider, renderPass);
        }

        public void Dispose()
        {
            _registry.Dispose();
        }
    }
}
