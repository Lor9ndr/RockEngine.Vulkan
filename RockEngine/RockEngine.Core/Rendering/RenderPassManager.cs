using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;

namespace RockEngine.Core.Rendering
{
    public class RenderPassManager : IDisposable
    {
        private readonly RenderingContext _context;
        private readonly Dictionary<string, VkRenderPass> _renderPasses;

        public RenderPassManager(RenderingContext context)
        {
            _context = context;
            _renderPasses = new Dictionary<string, VkRenderPass>();
        }

        public unsafe VkRenderPass CreateRenderPass(string name, SubpassDescription[] subpasses, AttachmentDescription[] attachments, SubpassDependency[] dependencies)
        {
            var renderPassInfo = new RenderPassCreateInfo
            {
                SType = StructureType.RenderPassCreateInfo,
                AttachmentCount = (uint)attachments.Length,
                PAttachments = (AttachmentDescription*)Unsafe.AsPointer(ref attachments[0]),
                SubpassCount = (uint)subpasses.Length,
                PSubpasses = (SubpassDescription*)Unsafe.AsPointer(ref subpasses[0]),
                DependencyCount = (uint)dependencies.Length,
                PDependencies = (SubpassDependency*)Unsafe.AsPointer(ref dependencies[0])
            };
            var renderPass = VkRenderPass.Create(_context, in renderPassInfo);
            _renderPasses[name] = renderPass;
            return renderPass;
        }

        public VkRenderPass? GetRenderPass(string name)
        {
            return _renderPasses.TryGetValue(name, out var renderPass) ? renderPass : null;
        }

        public void Dispose()
        {
            foreach (var renderPass in _renderPasses.Values)
            {
                renderPass.Dispose();
            }
            _renderPasses.Clear();
        }
    }

}
