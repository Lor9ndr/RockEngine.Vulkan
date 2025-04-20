using RockEngine.Core.Rendering.Managers;
using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering.Passes
{
    public abstract class RenderPass : IDisposable
    {
        protected readonly VulkanContext Context;
        protected readonly BindingManager BindingManager;

        protected RenderPass(VulkanContext context, BindingManager bindingManager)
        {
            Context = context;
            BindingManager = bindingManager;
        }

        public abstract Task Execute(VkCommandBuffer cmd, params object[] args);
        public virtual void Dispose() { }
    }
}
