using RockEngine.Vulkan;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RockEngine.Core.Rendering.Contexts
{
    public sealed class FrameContext : IDisposable
    {
        public readonly RenderContext Render;
        private readonly VulkanContext _vkContext;

        internal FrameContext(VulkanContext context)
        {
            _vkContext = context;
            //Render = new RenderContext(context);
        }

        public void Dispose()
        {
        }
    }
}
