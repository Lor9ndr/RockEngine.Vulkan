using RockEngine.Core.Builders;
using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering.Passes
{
    public interface IRenderSubPass
    {
        uint Order { get; }

        Task Execute(VkCommandBuffer cmd, params object[] args);
        void Dispose();
        /// <summary>
        /// Initialized after the renderpass builded it's renderpass of attached to it subpass
        /// </summary>
        public void Initilize();

        void SetupAttachmentDescriptions(RenderPassBuilder builder);
        void SetupSubpassDescription(RenderPassBuilder.SubpassConfigurer subpass);
        void SetupDependencies(RenderPassBuilder builder, uint subpassIndex);
    }
}
