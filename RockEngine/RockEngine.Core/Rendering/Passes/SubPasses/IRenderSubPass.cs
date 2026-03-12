using RockEngine.Core.Builders;
using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering.Passes.SubPasses
{
    public interface IRenderSubPass
    {
        static virtual uint Order { get; } = 0;
        static virtual string Name { get; } = "UNDEFINED";

        SubPassMetadata GetMetadata();

        void Execute(UploadBatch cmd, params object[] args);
        void Dispose();
        /// <summary>
        /// Initialized after the renderpass builded it's renderpass of attached to it subpass
        /// </summary>
        public void Initilize();

        void SetupAttachmentDescriptions(RenderPassBuilder builder);
        void SetupSubpassDescription(RenderPassBuilder.SubpassConfigurer subpass);
        void SetupDependencies(RenderPassBuilder builder, uint subpassIndex);
    }
    public readonly record struct SubPassMetadata(uint Order, string Name);
}
